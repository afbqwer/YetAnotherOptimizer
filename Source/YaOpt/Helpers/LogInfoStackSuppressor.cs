using System;
using System.Reflection;
using HarmonyLib;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Marks Info-level logging so the Waylith formatter skips stack-trace capture.
	/// </summary>
	/// <remarks>
	/// Vanilla captures a full <see cref="System.Diagnostics.StackTrace"/> with file info for every
	/// log level inside WaylithLogFormatter.Write. Info messages do not need it and the capture costs
	/// several microseconds plus several KB per message. The formatter only checks three inputs:
	/// EnableStackTraces, FileLog.IsCapturingStacks and LogStream.HasListeners. Suppression keeps a
	/// per-thread depth while LogCategory.Info runs, forces the two getters to return false during
	/// that scope, and disables EnableStackTraces once so the OR condition cannot fall through.
	/// Warning and Error keep their stacks in Player.log and in the log window.
	/// The scope relies on the logging providers formatting synchronously, which the Waylith
	/// console and file providers do.
	/// </remarks>
	internal static class LogInfoStackSuppressor
	{
		[ThreadStatic] private static int _infoDepth;

		private static bool _available;
		private static bool _warned;
		private static bool _announced;
		private static MethodBase _infoMethod;
		private static MethodBase _isCapturingStacksMethod;
		private static MethodBase _hasListenersMethod;
		private static FieldInfo _enableStackTracesField;
		private static bool? _originalEnableStackTraces;

		internal static bool Suppressed => _infoDepth > 0;

		internal static MethodBase InfoMethod => _infoMethod;

		internal static MethodBase IsCapturingStacksMethod => _isCapturingStacksMethod;

		internal static MethodBase HasListenersMethod => _hasListenersMethod;

		internal static void EnterInfo()
		{
			_infoDepth++;
		}

		internal static void ExitInfo()
		{
			if (_infoDepth > 0)
				_infoDepth--;
		}

		/// <summary>
		/// Resolves the engine members and applies or restores the console stack switch.
		/// Called from every patch class Prepare so one missing member disables all three patches.
		/// </summary>
		internal static bool Prepare()
		{
			var enabled = YaOptGlobal.Settings?.OptInfoLogNoStack.Enabled == true;
			if (Resolve() && enabled)
			{
				DisableEngineStackTraces();
				if (!_announced)
				{
					_announced = true;
					YaOptMod.Debug("Info stack suppression enabled.");
				}
				return true;
			}

			RestoreEngineStackTraces();
			return false;
		}

		private static bool Resolve()
		{
			if (_available)
				return true;

			try
			{
				var logCategory = AccessTools.TypeByName("Brutal.Logging.LogCategory");
				var fileLog = AccessTools.TypeByName("Waylith.Logging.FileLog");
				var logStream = AccessTools.TypeByName("Waylith.Logging.LogStream");
				var formatter = AccessTools.TypeByName("Waylith.Logging.WaylithLogFormatter");

				_infoMethod = FindInfoMethod(logCategory);
				_isCapturingStacksMethod = AccessTools.DeclaredMethod(fileLog, "get_IsCapturingStacks");
				_hasListenersMethod = AccessTools.DeclaredMethod(logStream, "get_HasListeners");
				_enableStackTracesField = formatter?.GetField("EnableStackTraces",
					BindingFlags.Public | BindingFlags.Static);

				_available = _infoMethod != null &&
							_isCapturingStacksMethod != null &&
							_hasListenersMethod != null &&
							_enableStackTracesField != null;
			}
			catch (Exception ex)
			{
				_available = false;
				WarnOnce("resolving engine log members threw: " + ex);
			}

			if (!_available)
				WarnOnce("engine log members were not found; Info stack suppression stays off.");
			return _available;
		}

		private static MethodBase FindInfoMethod(Type logCategory)
		{
			if (logCategory == null)
				return null;

			MethodBase found = null;
			foreach (var method in logCategory.GetMethods(BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
				BindingFlags.DeclaredOnly))
			{
				if (method.Name != "Info" || method.ReturnType != typeof(void))
					continue;

				var parameters = method.GetParameters();
				if (parameters.Length != 4 ||
					parameters[0].ParameterType.Name != "LogString`1" ||
					parameters[1].ParameterType != typeof(string) ||
					parameters[2].ParameterType != typeof(string) ||
					parameters[3].ParameterType != typeof(int))
				{
					continue;
				}

				if (found != null)
					return null;
				found = method;
			}

			return found;
		}

		private static void DisableEngineStackTraces()
		{
			if (_enableStackTracesField == null)
				return;

			if (!_originalEnableStackTraces.HasValue)
				_originalEnableStackTraces = (bool)_enableStackTracesField.GetValue(null);
			_enableStackTracesField.SetValue(null, false);
		}

		private static void RestoreEngineStackTraces()
		{
			if (_enableStackTracesField == null || !_originalEnableStackTraces.HasValue)
				return;

			_enableStackTracesField.SetValue(null, _originalEnableStackTraces.Value);
			_originalEnableStackTraces = null;
		}

		private static void WarnOnce(string message)
		{
			if (_warned)
				return;
			_warned = true;
			YaOptMod.Warning("[YaOpt] Info stack suppression disabled: " + message);
		}
	}
}