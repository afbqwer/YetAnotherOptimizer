// YaOptVeh.c - YetAnotherOptimizer native crash reporter for RimWorld 1.7 (CoreCLR / WaylithClr).
//
// Why this must be native: on CoreCLR every "native -> managed" entry (a marshaled managed
// delegate or an [UnmanagedCallersOnly] method) passes JIT_ReversePInvokeEnter, which
// fail-fasts the whole process (ExecutionEngineException 0x80131506) when the thread is in
// cooperative mode. A stack overflow always happens in cooperative mode, and VEH dispatch
// runs on the faulting thread with its current mode - so a pure C# mod can never handle
// these in-process. Only real native code can.
//
// What this reports, and what it cannot:
//   Reported (all reach user-mode exception dispatch, all terminate the process when
//   nothing handles them):
//     0xC0000005 STATUS_ACCESS_VIOLATION        - bad pointer dereference, or a jump into
//                                                 non-executable memory (op == 8)
//     0xC000001D STATUS_ILLEGAL_INSTRUCTION     - static CPU ISA assumption (AVX2/AVX512)
//     0xC00000FD STATUS_STACK_OVERFLOW          - infinite recursion
//     0xC0000006 STATUS_IN_PAGE_ERROR           - page could not be read (corrupt file,
//                                                 failed I/O, offline placeholder)
//     0xC0000096 STATUS_PRIVILEGED_INSTRUCTION  - ring-0 instruction executed in user mode
//   Not reported, by design:
//     fast-fail 0xC0000409 / CLR fail-fast      - the kernel terminates the process without
//                                                 ever returning to user mode, so no VEH,
//                                                 SEH or managed handler runs. Only a dump
//                                                 (written by the kernel) captures it.
//     0xE0434352 hosted exceptions              - almost always caught; AppDomain
//                                                 .FirstChanceException in C# gives far
//                                                 better data (type, message, stack).
//     0xC0000094 divide by zero                 - CLR converts it to a catchable
//                                                 DivideByZeroException; too noisy.
//
// Callback discipline (it runs for EVERY first-chance exception on EVERY thread):
//   1. one mask test rejects every code outside 0xC000xxxx - this is what keeps the
//      highest-frequency case (hosted exceptions) off the rest of the path;
//   2. a short linear match picks the slot, only then does anything atomic happen;
//   3. a per-code quota caps how many times a record is written; beyond the quota the
//      callback degrades to a single counter increment and returns.
// A record carries the fault site (RIP + module+offset, access kind and address) and, for
// stack overflow only, scans the top of the faulting stack for module-backed pointers. That
// scan costs ~0.4ms because every candidate needs a module lookup, so it is never done for
// the other codes. The callback always returns EXCEPTION_CONTINUE_SEARCH: the process is
// going to die either way, this only records where it happened.
//
// Built with the static CRT (/MT) so the DLL has no runtime dependencies.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define YAO_MAX_RECORDS_PER_CODE 4
#define YAO_LOG_BUF_SIZE 32768u
#define YAO_STACK_SCAN_BYTES 16384u

#define YAO_ACCESS_VIOLATION 0xC0000005u
#define YAO_IN_PAGE_ERROR 0xC0000006u
#define YAO_ILLEGAL_INSTRUCTION 0xC000001Du
#define YAO_PRIVILEGED_INSTRUCTION 0xC0000096u
#define YAO_STACK_OVERFLOW 0xC00000FDu

typedef struct
{
	DWORD code;
	const char *title;
	LONG total;
	LONG written;
} YaoCodeSlot;

// Order matters only for probe count; access violations come first because they are by far
// the most common real crash in a RimWorld mod list.
static YaoCodeSlot g_slots[] = {
	{ YAO_ACCESS_VIOLATION, "access violation", 0, 0 },
	{ YAO_ILLEGAL_INSTRUCTION, "illegal instruction", 0, 0 },
	{ YAO_STACK_OVERFLOW, "stack overflow", 0, 0 },
	{ YAO_IN_PAGE_ERROR, "in-page error", 0, 0 },
	{ YAO_PRIVILEGED_INSTRUCTION, "privileged instruction", 0, 0 },
};

#define YAO_SLOT_COUNT (sizeof(g_slots) / sizeof(g_slots[0]))

static HANDLE g_logFile = INVALID_HANDLE_VALUE;
static volatile LONG g_busy = 0;
static char g_buf[YAO_LOG_BUF_SIZE];

#define YAO_LOG_PATH_MAX 512
static wchar_t g_logPath[YAO_LOG_PATH_MAX];
static int g_logPathSet = 0;
static int g_headerWritten = 0;

// Minimal append helpers (no CRT, no heap). dst always stays <= end - 2 so a caller can
// always terminate with "\r\n\0" afterwards.
static char *AppendStr(char *dst, const char *end, const char *s)
{
	while (*s && dst < end)
		*dst++ = *s++;
	return dst;
}

static char *AppendHex(char *dst, const char *end, unsigned long long v)
{
	char tmp[16];
	int i = 0;
	do
	{
		unsigned d = (unsigned)(v & 0xF);
		tmp[i++] = (char)(d < 10 ? '0' + d : 'a' + d - 10);
		v >>= 4;
	} while (v != 0 && i < 16);
	while (i > 0 && dst < end)
		*dst++ = tmp[--i];
	return dst;
}

static char *AppendDec(char *dst, const char *end, unsigned long v)
{
	char tmp[12];
	int i = 0;
	do
	{
		tmp[i++] = (char)('0' + (v % 10));
		v /= 10;
	} while (v != 0 && i < 12);
	while (i > 0 && dst < end)
		*dst++ = tmp[--i];
	return dst;
}

static char *AppendPad2(char *dst, const char *end, WORD v)
{
	if (dst < end)
		*dst++ = (char)('0' + (v / 10) % 10);
	if (dst < end)
		*dst++ = (char)('0' + v % 10);
	return dst;
}

static char *AppendTimeStamp(char *dst, const char *end)
{
	SYSTEMTIME st;
	GetLocalTime(&st);
	dst = AppendPad2(dst, end, st.wMonth);
	if (dst < end)
		*dst++ = '-';
	dst = AppendPad2(dst, end, st.wDay);
	if (dst < end)
		*dst++ = ' ';
	dst = AppendPad2(dst, end, st.wHour);
	if (dst < end)
		*dst++ = ':';
	dst = AppendPad2(dst, end, st.wMinute);
	if (dst < end)
		*dst++ = ':';
	dst = AppendPad2(dst, end, st.wSecond);
	return dst;
}

// Resolves addr against loaded modules and appends " name.dll+0xrva" (ASCII best effort).
static char *AppendModuleRef(char *dst, const char *end, void *addr)
{
	HMODULE mod = NULL;
	wchar_t path[MAX_PATH];
	DWORD n;
	const wchar_t *name;
	DWORD k;

	if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
								GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
							(LPCWSTR)addr, &mod))
		return dst;

	n = GetModuleFileNameW(mod, path, MAX_PATH);
	name = path;
	for (k = 0; k < n; k++)
		if (path[k] == L'\\' || path[k] == L'/')
			name = path + k + 1;

	dst = AppendStr(dst, end, " ");
	for (k = 0; name[k] && dst < end; k++)
		*dst++ = (name[k] >= 0x20 && name[k] < 127) ? (char)name[k] : '?';
	dst = AppendStr(dst, end, "+0x");
	dst = AppendHex(dst, end, (unsigned long long)((unsigned char *)addr - (unsigned char *)mod));
	return dst;
}

static const char *AccessKind(ULONG_PTR op)
{
	switch (op)
	{
	case 0:
		return "read";
	case 1:
		return "write";
	case 8:
		return "execute";
	default:
		return "other";
	}
}

// Opens the log file and writes the header once. The file is opened at install time while the
// stack is intact - CreateFileW needs real stack, so doing it lazily fails exactly when a
// stack overflow makes the record most valuable. Nothing is written here until the first
// record, and the managed side deletes the file on exit if it stayed empty.
static int EnsureLogFile(void)
{
	char *dst;
	const char *end;
	DWORD written = 0;

	if (g_logFile == INVALID_HANDLE_VALUE)
	{
		if (!g_logPathSet)
			return 0;
		// FILE_SHARE_DELETE lets the managed side delete this file on exit when nothing was ever
		// recorded; without it the delete would fail on the handle we are holding.
		g_logFile = CreateFileW(g_logPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE, NULL,
								CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
		if (g_logFile == INVALID_HANDLE_VALUE)
			return 0;
	}
	if (g_headerWritten)
		return 1;

	dst = g_buf;
	end = g_buf + YAO_LOG_BUF_SIZE - 2;
	dst = AppendStr(dst, end, "YaOpt.NativeVEH native crash reporter\r\n"
							  "reports: access violation, illegal instruction, stack overflow, "
							  "in-page error, privileged instruction\r\n"
							  "cannot report: CLR fail-fast (0xC0000409) - the kernel never returns "
							  "to user mode, use a dump for those\r\n");
	WriteFile(g_logFile, g_buf, (DWORD)(dst - g_buf), &written, NULL);
	g_headerWritten = 1;
	return 1;
}

// Stack-overflow only: walk the top of the faulting stack and dump every value that points
// into a loaded module. With an exhausted stack this is the only way to get frame candidates
// without a dump. Deliberately not used for the other codes - each candidate costs a module
// lookup (~200ns), so a 16KB scan is ~0.4ms, which is visible inside a frame.
//
// Consecutive duplicates are collapsed into " xN". Infinite recursion produces thousands of
// identical return addresses, and without collapsing them the 32KB buffer fills with one
// repeated frame before any of the interesting ones are reached.
static char *ScanStackForCandidates(char *dst, const char *end, CONTEXT *ctx)
{
	const unsigned long long *p;
	const unsigned long long *scanEnd;
	void *prev = NULL;
	unsigned repeat = 0;
	int hasPrev = 0;

	if (!ctx)
		return dst;

	dst = AppendStr(dst, end, "module-backed pointers in the top stack region "
							  "(candidates; managed frames resolve only via a full dump):\r\n");

	p = (const unsigned long long *)(ctx->Rsp & ~(unsigned long long)7);
	scanEnd = p + YAO_STACK_SCAN_BYTES / sizeof(unsigned long long);
	for (; p < scanEnd && dst < end; p++)
	{
		void *addr = (void *)*p;
		HMODULE mod = NULL;

		if (!addr)
			continue;
		if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
									GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
								(LPCWSTR)addr, &mod))
			continue;

		if (hasPrev && addr == prev)
		{
			repeat++;
			continue;
		}
		if (hasPrev)
		{
			if (repeat)
			{
				dst = AppendStr(dst, end, " x");
				dst = AppendDec(dst, end, repeat + 1u);
			}
			dst = AppendStr(dst, end, "\r\n");
			repeat = 0;
		}

		prev = addr;
		hasPrev = 1;
		dst = AppendStr(dst, end, "  0x");
		dst = AppendHex(dst, end, (unsigned long long)(unsigned char *)addr);
		dst = AppendModuleRef(dst, end, addr);
	}

	if (hasPrev)
	{
		if (repeat)
		{
			dst = AppendStr(dst, end, " x");
			dst = AppendDec(dst, end, repeat + 1u);
		}
		dst = AppendStr(dst, end, "\r\n");
	}
	return dst;
}

static LONG CALLBACK VehCallback(PEXCEPTION_POINTERS info)
{
	EXCEPTION_RECORD *rec;
	YaoCodeSlot *slot = NULL;
	DWORD code;
	unsigned i;
	int isStackOverflow;

	if (!info)
		return EXCEPTION_CONTINUE_SEARCH;
	rec = info->ExceptionRecord;
	if (!rec)
		return EXCEPTION_CONTINUE_SEARCH;

	code = rec->ExceptionCode;

	// Every code we handle lives in the 0xC000xxxx status range, while hosted exceptions
	// (0xE0434352) - the highest-frequency thing raised in RimWorld - do not. One mask test
	// keeps them off everything below.
	if ((code & 0xFFFF0000u) != 0xC0000000u)
		return EXCEPTION_CONTINUE_SEARCH;

	for (i = 0; i < YAO_SLOT_COUNT; i++)
	{
		if (g_slots[i].code == code)
		{
			slot = &g_slots[i];
			break;
		}
	}
	if (!slot)
		return EXCEPTION_CONTINUE_SEARCH;

	InterlockedIncrement(&slot->total);

	// Quota gate first: once a code has been recorded enough times, the callback must cost
	// nothing beyond the increment above. A time-windowed quota would turn a sustained flood
	// into a permanent cost, so this is a lifetime cap.
	if (slot->written >= YAO_MAX_RECORDS_PER_CODE)
		return EXCEPTION_CONTINUE_SEARCH;

	// Single-flight guard covers every code, not just stack overflow: g_buf and slot->written
	// are shared, and several threads can fault at the same time.
	if (InterlockedExchange(&g_busy, 1) != 0)
		return EXCEPTION_CONTINUE_SEARCH;

	isStackOverflow = (code == YAO_STACK_OVERFLOW);

	// Everything below must stay small-frame and must never touch managed code. __try keeps a
	// stray fault inside the reporter from turning into a second, log-less crash.
	__try
	{
		char *dst;
		const char *end;
		CONTEXT *ctx;
		void *faultAddr;
		DWORD written = 0;

		if (!EnsureLogFile())
			__leave;

		InterlockedIncrement(&slot->written);
		dst = g_buf;
		end = g_buf + YAO_LOG_BUF_SIZE - 2;
		ctx = info->ContextRecord;
		faultAddr = ctx ? (void *)ctx->Rip : (void *)rec->ExceptionAddress;

		dst = AppendStr(dst, end, "[");
		dst = AppendTimeStamp(dst, end);
		dst = AppendStr(dst, end, "] #");
		dst = AppendDec(dst, end, (unsigned long)slot->written);
		dst = AppendStr(dst, end, "/");
		dst = AppendDec(dst, end, YAO_MAX_RECORDS_PER_CODE);
		dst = AppendStr(dst, end, " 0x");
		dst = AppendHex(dst, end, code);
		dst = AppendStr(dst, end, " ");
		dst = AppendStr(dst, end, slot->title);
		dst = AppendStr(dst, end, "  (occurrence ");
		dst = AppendDec(dst, end, (unsigned long)slot->total);
		dst = AppendStr(dst, end, ")\r\n  tid=0x");
		dst = AppendHex(dst, end, (unsigned long long)GetCurrentThreadId());
		dst = AppendStr(dst, end, " rip=0x");
		dst = AppendHex(dst, end, (unsigned long long)(unsigned char *)faultAddr);
		dst = AppendModuleRef(dst, end, faultAddr);

		// Access violations and in-page errors carry the operation kind and the address that
		// could not be touched. A read/write on a low address is usually a managed null
		// dereference that the CLR is about to convert into a catchable
		// NullReferenceException - flagged as "low-address" so it can be told apart from a
		// real wild-pointer crash at a glance.
		if (code == YAO_ACCESS_VIOLATION || code == YAO_IN_PAGE_ERROR)
		{
			ULONG_PTR op = rec->NumberParameters > 0 ? rec->ExceptionInformation[0] : 0;
			ULONG_PTR addr = rec->NumberParameters > 1 ? rec->ExceptionInformation[1] : 0;

			dst = AppendStr(dst, end, "\r\n  op=");
			dst = AppendStr(dst, end, AccessKind(op));
			dst = AppendStr(dst, end, " address=0x");
			dst = AppendHex(dst, end, (unsigned long long)addr);
			if (op != 8 && addr < 0x10000)
				dst = AppendStr(dst, end, " (low address; possibly a managed NullReference)");
			if (code == YAO_IN_PAGE_ERROR && rec->NumberParameters > 2)
			{
				dst = AppendStr(dst, end, "\r\n  ntstatus=0x");
				dst = AppendHex(dst, end, (unsigned long long)rec->ExceptionInformation[2]);
			}
		}

		dst = AppendStr(dst, end, "\r\n");

		if (isStackOverflow)
			dst = ScanStackForCandidates(dst, end, ctx);

		dst = AppendStr(dst, end, "----------------------------------------\r\n");

		WriteFile(g_logFile, g_buf, (DWORD)(dst - g_buf), &written, NULL);
		FlushFileBuffers(g_logFile);
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		// Our own access violation while formatting: nothing more we can do here.
	}

	// Stack overflow stays latched: the process is already dying, and re-entering the reporter
	// from inside an exhausted stack is how you get an unrecoverable loop.
	if (!isStackOverflow)
		InterlockedExchange(&g_busy, 0);

	return EXCEPTION_CONTINUE_SEARCH;
}

// Installs the crash reporter. Returns 0 on success, 1 if already installed, negative on
// failure (-1 VEH registration failed, -2 the log file could not be created, -3 bad argument).
__declspec(dllexport) int __stdcall YaOptVehInstall(const wchar_t *logPath)
{
	unsigned i = 0;

	if (!logPath || !logPath[0])
		return -3;
	if (g_logPathSet || g_logFile != INVALID_HANDLE_VALUE)
		return 1;

	while (logPath[i] && i < YAO_LOG_PATH_MAX - 1)
	{
		g_logPath[i] = logPath[i];
		i++;
	}
	if (i == 0)
		return -2;
	g_logPath[i] = 0;
	g_logPathSet = 1;

	// Open the handle now rather than on the first record: CreateFileW needs a healthy stack,
	// and a stack overflow is precisely when the record must still be written.
	g_logFile = CreateFileW(g_logPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_DELETE, NULL,
							CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
	if (g_logFile == INVALID_HANDLE_VALUE)
	{
		g_logPathSet = 0;
		return -2;
	}

	if (!AddVectoredExceptionHandler(1, VehCallback))
	{
		CloseHandle(g_logFile);
		g_logFile = INVALID_HANDLE_VALUE;
		g_logPathSet = 0;
		return -1;
	}
	return 0;
}

// Enumerates the per-code counters for the managed side, which reports them through the game
// log on exit. Returns 0 when index is out of range. Nothing is written to disk here, so a
// session without a fault leaves no file behind.
__declspec(dllexport) int __stdcall YaOptVehGetSlot(unsigned index, unsigned *code,
													LONG *total, LONG *written)
{
	if (index >= YAO_SLOT_COUNT)
		return 0;
	if (code)
		*code = g_slots[index].code;
	if (total)
		*total = g_slots[index].total;
	if (written)
		*written = g_slots[index].written;
	return 1;
}

// --- Self tests ------------------------------------------------------------
// Both spawn a native thread that faults in a controlled way and leave the process to die.
// Check the log file afterwards.

#pragma warning(disable: 4717) // Recurse: infinite recursion is the point of the stack test.

static int Recurse(int n)
{
	volatile char buf[256];
	buf[0] = (char)n;
	buf[255] = (char)(n ^ 0x5A);
	return Recurse(n + 1) + buf[0];
}

static DWORD WINAPI SelfTestThread(LPVOID arg)
{
	(void)arg;
	return (DWORD)Recurse(0);
}

__declspec(dllexport) int __stdcall YaOptVehSelfTest(void)
{
	HANDLE t = CreateThread(NULL, 0, SelfTestThread, NULL, 0, NULL);
	if (!t)
		return -1;
	WaitForSingleObject(t, INFINITE);
	return -2;
}

static DWORD WINAPI SelfTestAvThread(LPVOID arg)
{
	volatile int *p;
	(void)arg;
	// 0x10 rather than NULL: far enough that the store cannot be folded away, near enough
	// that the reporter flags it as a likely managed null dereference.
	p = (volatile int *)(ULONG_PTR)0x10;
	*p = 1;
	return *p;
}

__declspec(dllexport) int __stdcall YaOptVehSelfTestAv(void)
{
	HANDLE t = CreateThread(NULL, 0, SelfTestAvThread, NULL, 0, NULL);
	if (!t)
		return -1;
	WaitForSingleObject(t, INFINITE);
	return -2;
}

// rundll32-compatible entries for standalone testing without the game:
//   rundll32 YaOpt.NativeVEH.dll,YaOptVehRundllSelfTestW   <logPath>
//   rundll32 YaOpt.NativeVEH.dll,YaOptVehRundllSelfTestAvW <logPath>
__declspec(dllexport) void CALLBACK YaOptVehRundllSelfTestW(HWND hwnd, HINSTANCE hinst, LPWSTR cmdline, int show)
{
	(void)hwnd;
	(void)hinst;
	(void)show;
	if (YaOptVehInstall(cmdline) != 0)
		return;
	YaOptVehSelfTest();
}

__declspec(dllexport) void CALLBACK YaOptVehRundllSelfTestAvW(HWND hwnd, HINSTANCE hinst, LPWSTR cmdline, int show)
{
	(void)hwnd;
	(void)hinst;
	(void)show;
	if (YaOptVehInstall(cmdline) != 0)
		return;
	YaOptVehSelfTestAv();
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
	(void)inst;
	(void)reason;
	(void)reserved;
	return TRUE;
}
