using System.Reflection;
using HarmonyLib;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Marks game Info messages so the Waylith formatter skips stack-trace capture.
	/// </summary>
	/// <remarks>
	/// This anchors the scope at the game entry point as well as <c>LogCategory.Info</c>,
	/// because the small Info method may be inlined into its callers by the JIT.
	/// </remarks>
	/// <seealso cref="LogInfoStackSuppressor"/>
	/// <seealso cref="YaOptSettings.OptInfoLogNoStack"/>
	[HarmonyPatch(typeof(Verse.Log), nameof(Verse.Log.Message), typeof(string))]
	internal static class Verse_Log_Message
	{
		private static bool Prepare()
		{
			return LogInfoStackSuppressor.Prepare();
		}

		private static void Prefix()
		{
			LogInfoStackSuppressor.EnterInfo();
		}

		private static void Finalizer()
		{
			LogInfoStackSuppressor.ExitInfo();
		}
	}

	/// <summary>
	/// Marks Info-level log calls so the Waylith formatter skips stack-trace capture.
	/// </summary>
	/// <remarks>
	/// Only the Info entry point sets the suppression scope. Warning, Error and Critical keep
	/// their stack traces. The scope is per-thread because logging can come from load threads.
	/// </remarks>
	/// <seealso cref="LogInfoStackSuppressor"/>
	/// <seealso cref="YaOptSettings.OptInfoLogNoStack"/>
	[HarmonyPatch]
	internal static class Brutal_LogCategory_Info
	{
		private static MethodBase TargetMethod()
		{
			return LogInfoStackSuppressor.InfoMethod;
		}

		private static bool Prepare()
		{
			return LogInfoStackSuppressor.Prepare();
		}

		private static void Prefix()
		{
			LogInfoStackSuppressor.EnterInfo();
		}

		private static void Finalizer()
		{
			LogInfoStackSuppressor.ExitInfo();
		}
	}

	/// <summary>
	/// Pretends the file log is not capturing stacks while an Info message is being formatted.
	/// </summary>
	/// <seealso cref="LogInfoStackSuppressor"/>
	[HarmonyPatch]
	internal static class Waylith_FileLog_IsCapturingStacks
	{
		private static MethodBase TargetMethod()
		{
			return LogInfoStackSuppressor.IsCapturingStacksMethod;
		}

		private static bool Prepare()
		{
			return LogInfoStackSuppressor.Prepare();
		}

		private static bool Prefix(ref bool __result)
		{
			if (!LogInfoStackSuppressor.Suppressed)
				return true;

			__result = false;
			return false;
		}
	}

	/// <summary>
	/// Pretends the log stream has no listeners while an Info message is being formatted.
	/// </summary>
	/// <remarks>
	/// The stream still publishes the message; only the formatter's stack-capture condition reads
	/// this getter. Without this, the "has listeners" term would keep stack capture enabled even
	/// after EnableStackTraces and CaptureStackTraces are off.
	/// </remarks>
	/// <seealso cref="LogInfoStackSuppressor"/>
	[HarmonyPatch]
	internal static class Waylith_LogStream_HasListeners
	{
		private static MethodBase TargetMethod()
		{
			return LogInfoStackSuppressor.HasListenersMethod;
		}

		private static bool Prepare()
		{
			return LogInfoStackSuppressor.Prepare();
		}

		private static bool Prefix(ref bool __result)
		{
			if (!LogInfoStackSuppressor.Suppressed)
				return true;

			__result = false;
			return false;
		}
	}
}