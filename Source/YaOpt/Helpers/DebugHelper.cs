using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Verse;

namespace YaOpt.Helpers
{
	public static class DebugHelper
	{
		private const int MAX_STACK_TOP_PRINT_COUNT = 48;
		private const int MAX_STACK_BOTTOM_PRINT_COUNT = 36;
		private const string NativeShimDllName = "YaOpt.NativeVEH.dll";

		private static bool _crashReporterInstalled;
		private static string _crashLogPath;

		public static void Init()
		{
			try
			{
				AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
					YaOptGlobal.Settings.VectoredExceptionHandler.Enabled)
				{
					InstallCrashReporter();
				}
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Failed to initialize DebugHelper. " +
								 "However, this does not affect the gameplay.\n" +
								 e.ToString());
			}
		}

		/// <summary>
		/// Loads the native VEH shim and installs the crash reporter.
		/// </summary>
		/// <remarks>
		/// The callback must be pure native code: every managed callback (a marshaled delegate or
		/// an [UnmanagedCallersOnly] method) fails the JIT_ReversePInvokeEnter cooperative-mode
		/// check when entered from a thread inside managed code, and a stack overflow is always
		/// exactly that. Everything the shim reports is beyond the reach of managed code: the CLR
		/// never surfaces access violations, illegal instructions or stack overflows to
		/// AppDomain.UnhandledException. It writes the fault site to a log file in the save data
		/// folder, then lets the CLR fail-fast proceed. CLR fail-fast itself (0xC0000409) is not
		/// reachable from user mode and still needs a dump.
		/// Source: Source/NativeVEH/YaOptVeh.c.
		/// </remarks>
		private static void InstallCrashReporter()
		{
			// The shim lives in 1.7/Native, NOT in Assemblies: the game loads every dll in
			// Assemblies as a managed mod assembly, and a native dll would error each startup.
			var dllPath = Path.GetFullPath(Path.Combine(
				Path.GetDirectoryName(typeof(DebugHelper).Assembly.Location), "..", "Native", NativeShimDllName));
			if (!File.Exists(dllPath))
			{
				YaOptMod.Warning($"{NativeShimDllName} not found next to YaOpt.dll; " +
								 "crash reporting is unavailable.");
				return;
			}

			// Load explicitly: mod assembly directories are not on the native loader search path.
			NativeLibrary.Load(dllPath);

			var logPath = Path.Combine(GenFilePaths.SaveDataFolderPath,
									   $"YaOpt_Crash_{Environment.ProcessId}.log");
			var rc = YaOptVehInstall(logPath);
			switch (rc)
			{
				case 0:
					_crashReporterInstalled = true;
					_crashLogPath = logPath;
					AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
					YaOptMod.Log("Crash reporting installed. Native faults (access violation, illegal " +
								 "instruction, stack overflow, in-page error, privileged instruction) " +
								 "are recorded to " + logPath);
					break;
				case 1:
					_crashReporterInstalled = true;
					_crashLogPath = logPath;
					AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
					YaOptMod.Warning("Crash reporting was already installed.");
					break;
				default:
					YaOptMod.Warning($"Crash reporting failed to install (error code {rc}).");
					break;
			}

			if (Environment.GetEnvironmentVariable("YAOPT_SO_TEST") == "1")
			{
				YaOptMod.Error("YAOPT_SO_TEST=1: deliberately overflowing a native thread now. " +
							   "The game is expected to die immediately; check the crash log afterwards.");
				YaOptVehSelfTest();
			}
			if (Environment.GetEnvironmentVariable("YAOPT_AV_TEST") == "1")
			{
				YaOptMod.Error("YAOPT_AV_TEST=1: deliberately dereferencing a bad pointer now. " +
							   "The game is expected to die immediately; check the crash log afterwards.");
				YaOptVehSelfTestAv();
			}
		}

		private static void OnProcessExit(object sender, EventArgs e)
		{
			if (!_crashReporterInstalled)
			{
				return;
			}
			try
			{
				// Per-code totals. Only 0xC000 status faults are counted here; hosted exceptions are
				// far more frequent but are not this reporter's business. Printed only when
				// something actually happened, so a clean session stays silent.
				var sb = new StringBuilder();
				var faults = 0;
				for (uint i = 0; ; i++)
				{
					if (YaOptVehGetSlot(i, out var code, out var total, out var written) == 0)
					{
						break;
					}
					if (total == 0)
					{
						continue;
					}
					faults += total;
					sb.Append("  ").Append(code.ToString("X8")).Append(": ")
						.Append(total).Append(" seen, ").Append(written).AppendLine(" recorded");
				}
				if (faults > 0)
				{
					YaOptMod.Log("Native fault counters at exit:\n" + sb);
				}
			}
			catch (Exception)
			{
				// Process is going away; nothing useful to report here.
			}

			TryDeleteEmptyLog();
		}

		/// <summary>
		/// Removes the crash log when nothing was ever written to it.
		/// </summary>
		/// <remarks>
		/// The native side opens the file at install time so that a stack overflow can still write
		/// to an existing handle. Without this cleanup every session would leave an empty file in
		/// the save data folder.
		/// </remarks>
		private static void TryDeleteEmptyLog()
		{
			if (string.IsNullOrEmpty(_crashLogPath))
			{
				return;
			}
			try
			{
				var info = new FileInfo(_crashLogPath);
				if (info.Exists && info.Length == 0)
				{
					info.Delete();
				}
			}
			catch (Exception)
			{
				// Locked or already gone: not worth reporting while the process is exiting.
			}
		}

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehInstall", CharSet = CharSet.Unicode)]
		private static extern int YaOptVehInstall(string logPath);

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehGetSlot")]
		private static extern int YaOptVehGetSlot(uint index, out uint code, out int total, out int written);

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehSelfTest")]
		private static extern int YaOptVehSelfTest();

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehSelfTestAv")]
		private static extern int YaOptVehSelfTestAv();

		private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			var stackTrace = new StackTrace();
			var sb = new StringBuilder()
				.AppendLine("=== Unhandled Exception ===")
				.AppendLine("YaOpt caught an unhandled exception, " +
							"which was not captured by the game for unknown reasons. " +
							"The game is likely to crash soon.")
				.AppendLine(e.ExceptionObject.ToString());
			PrintCallStack(stackTrace, sb);
			sb.AppendLine("================================");
			YaOptMod.Error(sb.ToString());
		}

		private static void PrintCallStack(StackTrace stackTrace, StringBuilder sb)
		{
			var i = 0;
			var count = stackTrace.FrameCount;
			var j = Math.Min(count, MAX_STACK_TOP_PRINT_COUNT);
			for (i = 0; i < j; i++)
			{
				var frame = stackTrace.GetFrame(i);
				PrintFrame(frame, sb);
			}
			if (i < count - MAX_STACK_BOTTOM_PRINT_COUNT)
			{
				sb.Append("... (").Append(count - i - MAX_STACK_BOTTOM_PRINT_COUNT).AppendLine(" more method calls)");
			}
			for (i = count - MAX_STACK_BOTTOM_PRINT_COUNT, j = count; i < j; i++)
			{
				var frame = stackTrace.GetFrame(i);
				PrintFrame(frame, sb);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void PrintFrame(StackFrame frame, StringBuilder sb)
		{
			var method = frame.GetMethod();
			sb.Append("	at ")
				.Append(method.DeclaringTypeName())
				.Append('.')
				.AppendLine(method.Name);
		}
	}
}
