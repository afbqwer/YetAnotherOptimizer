using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace YaOpt.Helpers
{
	public static class DebugHelper
	{
		private const int MAX_STACK_TOP_PRINT_COUNT = 48;
		private const int MAX_STACK_BOTTOM_PRINT_COUNT = 36;

		public static void Init()
		{
			try
			{
				AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Failed to initialize DebugHelper. " +
								 "However, this does not affect the gameplay.\n" +
								 e.ToString());
			}
		}

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
