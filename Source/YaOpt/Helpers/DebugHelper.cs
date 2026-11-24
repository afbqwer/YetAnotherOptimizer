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

		public static void Init()
		{
			try
			{
				AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
					YaOptGlobal.Settings.VectoredExceptionHandler.Enabled)
				{
					InstallStackOverflowLogger();
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
		/// Loads the native VEH shim and installs the stack overflow logger.
		/// </summary>
		/// <remarks>
		/// The callback must be pure native code: every managed callback (a marshaled delegate or
		/// an [UnmanagedCallersOnly] method) fails the JIT_ReversePInvokeEnter cooperative-mode
		/// check when entered from a thread inside managed code, and a stack overflow is always
		/// exactly that. The shim writes module+offset frame candidates for the overflowing
		/// thread to a log file in the save data folder, then lets the CLR fail-fast proceed.
		/// Source: Source/NativeVEH/YaOptVeh.c.
		/// </remarks>
		private static void InstallStackOverflowLogger()
		{
			// The shim lives in 1.7/Native, NOT in Assemblies: the game loads every dll in
			// Assemblies as a managed mod assembly, and a native dll would error each startup.
			var dllPath = Path.GetFullPath(Path.Combine(
				Path.GetDirectoryName(typeof(DebugHelper).Assembly.Location), "..", "Native", NativeShimDllName));
			if (!File.Exists(dllPath))
			{
				YaOptMod.Warning($"{NativeShimDllName} not found next to YaOpt.dll; " +
								 "stack overflow logging is unavailable.");
				return;
			}

			// Load explicitly: mod assembly directories are not on the native loader search path.
			NativeLibrary.Load(dllPath);

			var logPath = Path.Combine(GenFilePaths.SaveDataFolderPath,
									   $"YaOpt_StackOverflow_{Environment.ProcessId}.log");
			var rc = YaOptVehInstall(logPath);
			switch (rc)
			{
				case 0:
					YaOptMod.Log("Stack overflow logging installed. If the game ever dies from a " +
								 "stack overflow, native frame candidates are written to " + logPath);
					break;
				case 1:
					YaOptMod.Warning("Stack overflow logging was already installed.");
					break;
				default:
					YaOptMod.Warning($"Stack overflow logging failed to install (error code {rc}).");
					break;
			}

			if (Environment.GetEnvironmentVariable("YAOPT_SO_TEST") == "1")
			{
				YaOptMod.Error("YAOPT_SO_TEST=1: deliberately overflowing a native thread now. " +
							   "The game is expected to die immediately; check the stack overflow log afterwards.");
				YaOptVehSelfTest();
			}
		}

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehInstall", CharSet = CharSet.Unicode)]
		private static extern int YaOptVehInstall(string logPath);

		[DllImport("YaOpt.NativeVEH", EntryPoint = "YaOptVehSelfTest")]
		private static extern int YaOptVehSelfTest();

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
