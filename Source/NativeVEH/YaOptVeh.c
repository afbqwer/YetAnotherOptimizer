// YaOptVeh.c - YetAnotherOptimizer native VEH shim for RimWorld 1.7 (CoreCLR / WaylithClr).
//
// Why this must be native: on CoreCLR every "native -> managed" entry (a marshaled managed
// delegate or an [UnmanagedCallersOnly] method) passes JIT_ReversePInvokeEnter, which
// fail-fasts the whole process (ExecutionEngineException 0x80131506) when the thread is in
// cooperative mode. A stack overflow always happens in cooperative mode, and VEH dispatch
// runs on the faulting thread with its current mode - so a pure C# mod can never handle
// stack overflows in-process. Only real native code can.
//
// Callback discipline (it runs for EVERY first-chance exception on EVERY thread):
//   * non-stack-overflow: one comparison, return immediately.
//   * stack overflow: single-flight guard, then scan the top of the faulting thread's stack
//     for module-backed pointers and append "name.dll+0xrva" candidates to a pre-opened
//     log file, then EXCEPTION_CONTINUE_SEARCH so the CLR's own fail-fast (and createdump,
//     if configured) proceeds untouched. The process will die either way; this only
//     records where it happened.
//
// Built with the static CRT (/MT) so the DLL has no runtime dependencies. If you want a
// tiny freestanding build instead, the only obstacles are __C_specific_handler for the
// __try below and compiler-generated memmove - both avoidable, but not worth it.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define STACK_SCAN_BYTES 16384u
#define LOG_BUF_SIZE 32768u

#ifndef STATUS_STACK_OVERFLOW
#define STATUS_STACK_OVERFLOW 0xC00000FDu
#endif

static HANDLE g_logFile = INVALID_HANDLE_VALUE;
static volatile LONG g_busy = 0;
static char g_buf[LOG_BUF_SIZE];

// Minimal append helpers (no CRT, no heap). dst always stays <= end - 2 so we can
// always terminate with "\r\n\0" if needed.
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

// Resolves addr against loaded modules and appends " name.dll+0xrva" (ASCII best effort).
static char *AppendModuleRef(char *dst, const char *end, void *addr)
{
	HMODULE mod = NULL;
	if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
							GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
							(LPCWSTR)addr, &mod))
		return dst;
	wchar_t path[MAX_PATH];
	DWORD n = GetModuleFileNameW(mod, path, MAX_PATH);
	const wchar_t *name = path;
	for (DWORD k = 0; k < n; k++)
		if (path[k] == L'\\' || path[k] == L'/')
			name = path + k + 1;
	dst = AppendStr(dst, end, " ");
	for (DWORD k = 0; name[k] && dst < end; k++)
		*dst++ = (name[k] >= 0x20 && name[k] < 127) ? (char)name[k] : '?';
	dst = AppendStr(dst, end, "+0x");
	dst = AppendHex(dst, end, (unsigned long long)((unsigned char *)addr - (unsigned char *)mod));
	return dst;
}

static LONG CALLBACK VehCallback(PEXCEPTION_POINTERS info)
{
	if (!info || !info->ExceptionRecord ||
		info->ExceptionRecord->ExceptionCode != STATUS_STACK_OVERFLOW)
		return EXCEPTION_CONTINUE_SEARCH;
	if (InterlockedExchange(&g_busy, 1) != 0)
		return EXCEPTION_CONTINUE_SEARCH;

	// The faulting thread's stack is exhausted; everything below must stay small-frame
	// and must never touch managed code. __try keeps a stray fault inside the scan from
	// turning into a second, log-less crash.
	__try
	{
		if (g_logFile == INVALID_HANDLE_VALUE)
			__leave;

		char *dst = g_buf;
		const char *end = g_buf + LOG_BUF_SIZE - 2;
		CONTEXT *ctx = info->ContextRecord;

		dst = AppendStr(dst, end, "=== YaOpt: stack overflow detected ===\r\nthread id: 0x");
		dst = AppendHex(dst, end, (unsigned long long)GetCurrentThreadId());
		dst = AppendStr(dst, end, "\r\nrip: 0x");
		dst = AppendHex(dst, end, (unsigned long long)ctx->Rip);
		dst = AppendModuleRef(dst, end, (void *)ctx->Rip);
		dst = AppendStr(dst, end, "\r\nrsp: 0x");
		dst = AppendHex(dst, end, (unsigned long long)ctx->Rsp);
		dst = AppendStr(dst, end, "\r\nmodule-backed pointers in the top stack region "
								  "(candidates; managed frames resolve only via a full dump):\r\n");

		const unsigned long long *p = (const unsigned long long *)(ctx->Rsp & ~(unsigned long long)7);
		const unsigned long long *scanEnd = p + STACK_SCAN_BYTES / sizeof(unsigned long long);
		for (; p < scanEnd && dst < end; p++)
		{
			void *addr = (void *)*p;
			if (!addr)
				continue;
			HMODULE mod = NULL;
			if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
									GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
									(LPCWSTR)addr, &mod))
				continue;
			dst = AppendStr(dst, end, "  0x");
			dst = AppendHex(dst, end, (unsigned long long)(unsigned char *)addr);
			dst = AppendModuleRef(dst, end, addr);
			*dst++ = '\r';
			*dst++ = '\n';
		}
		dst = AppendStr(dst, end, "========================================\r\n");

		DWORD written = 0;
		WriteFile(g_logFile, g_buf, (DWORD)(dst - g_buf), &written, NULL);
		FlushFileBuffers(g_logFile);
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		// Our own access violation while scanning: nothing more we can do here.
	}
	return EXCEPTION_CONTINUE_SEARCH;
}

// Installs the stack overflow logger. Returns 0 on success, 1 if already installed,
// negative on failure (-1 VEH registration failed, -2 log file could not be created,
// -3 invalid argument).
__declspec(dllexport) int __stdcall YaOptVehInstall(const wchar_t *logPath)
{
	if (!logPath || !logPath[0])
		return -3;
	if (g_logFile != INVALID_HANDLE_VALUE)
		return 1;
	g_logFile = CreateFileW(logPath, GENERIC_WRITE, FILE_SHARE_READ, NULL,
							CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
	if (g_logFile == INVALID_HANDLE_VALUE)
		return -2;
	if (!AddVectoredExceptionHandler(1, VehCallback))
	{
		CloseHandle(g_logFile);
		g_logFile = INVALID_HANDLE_VALUE;
		return -1;
	}
	char *dst = g_buf;
	const char *end = g_buf + LOG_BUF_SIZE - 2;
	dst = AppendStr(dst, end, "YaOpt.NativeVEH installed (stack overflow logger)\r\n");
	DWORD written = 0;
	WriteFile(g_logFile, g_buf, (DWORD)(dst - g_buf), &written, NULL);
	return 0;
}

// --- Self test -------------------------------------------------------------
// Spawns a native thread that recurses until the stack overflows, exercising the whole
// chain (VEH dispatch -> log write -> process death). The caller process is expected to
// terminate; check the log file afterwards.

#pragma warning(disable: 4717) // Recurse: infinite recursion is the point of the self test.

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
	// Not reached in practice: the spawned thread overflows and the process dies.
	WaitForSingleObject(t, INFINITE);
	return -2;
}

// rundll32-compatible entry for standalone testing without the game:
//   rundll32 YaOpt.NativeVEH.dll,YaOptVehRundllSelfTestW <logPath>
// Installs the logger with <logPath> and immediately overflows a native thread.
// The process is expected to die; inspect <logPath> afterwards.
__declspec(dllexport) void CALLBACK YaOptVehRundllSelfTestW(HWND hwnd, HINSTANCE hinst, LPWSTR cmdline, int show)
{
	(void)hwnd;
	(void)hinst;
	(void)show;
	if (YaOptVehInstall(cmdline) != 0)
		return;
	YaOptVehSelfTest();
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
	(void)inst;
	(void)reason;
	(void)reserved;
	return TRUE;
}
