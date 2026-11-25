using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Threading;
using Verse;
using YaOpt.Helpers;
using YaOpt.Settings;

namespace YaOpt
{
	/// <summary>
	/// Provides global access to mod state, settings, and platform capabilities.
	/// </summary>
	/// <remarks>
	/// This static class serves as a central hub for accessing mod state and checking
	/// platform capabilities. It caches various flags and provides convenient access
	/// to the mod instance and settings.
	/// </remarks>
	public static class YaOptGlobal
	{
		/// <summary>
		/// Gets a value indicating whether debug logging is enabled.
		/// </summary>
		public static bool IsDebug => Settings?.DebugLogging == true;

		/// <summary>
		/// Gets a value indicating whether the Multiplayer mod is active.
		/// </summary>
		/// <remarks>
		/// Some optimizations are incompatible with multiplayer and are automatically disabled
		/// when this flag is true.
		/// </remarks>
		public static bool IsMultiplayer { get; internal set; }

		/// <summary>
		/// Gets a value indicating whether all required libraries have been loaded.
		/// </summary>
		public static bool IsLibraryLoaded { get; internal set; }

		/// <summary>
		/// Gets a value indicating whether the current thread is the Unity main thread.
		/// </summary>
		public static bool IsInMainThread => _isInMainThread;

		/// <summary>
		/// Gets a value indicating whether idle check throttle is enabled.
		/// </summary>
		/// <seealso cref="YaOpt.Helpers.IdleHelper"/>
		/// <seealso cref="YaOpt.YaOptSettings.OptIdleThrottle"/>
		public static bool IsIdleThrottleEnabled { get; internal set; }

		/// <summary>
		/// Gets the singleton mod instance.
		/// </summary>
		public static YaOptMod Mod => YaOptMod.Instance;

		/// <summary>
		/// Gets the mod settings.
		/// </summary>
		public static YaOptSettings Settings => YaOptMod.Instance.Settings;

		/// <summary>
		/// Gets the main Harmony instance for patching.
		/// </summary>
		public static Harmony Harmony => YaOptMod.Instance.Harmony;

		private static readonly Dictionary<string, bool> _modLookup = new Dictionary<string, bool>();

		private static readonly Dictionary<string, bool> _typeLookup = new Dictionary<string, bool>();

		[ThreadStatic]
		private static bool _isInMainThread;

		private static readonly Dictionary<OptimizationOption, bool> _optionSnapshot =
			new Dictionary<OptimizationOption, bool>();

		/// <summary>
		/// Checks whether a type with the specified full name exists in the loaded assemblies.
		/// </summary>
		/// <remarks>
		/// Results are cached for performance. This method is useful for soft dependency checks.
		/// </remarks>
		public static bool HasType(string typeFullName)
		{
			if (!_typeLookup.TryGetValue(typeFullName, out var result))
			{
				result = AccessTools.TypeByName(typeFullName) != null;
				_typeLookup[typeFullName] = result;
			}
			return result;
		}

		/// <summary>
		/// Checks whether a mod with the specified identifier is active.
		/// </summary>
		/// <remarks>
		/// Results are cached for performance. Use this method to conditionally enable
		/// compatibility features based on other mods' presence.
		/// </remarks>
		public static bool HasMod(string modId)
		{
			if (!_modLookup.TryGetValue(modId, out var result))
			{
				result = ModLister.GetActiveModWithIdentifier(modId, ignorePostfix: true) != null;
				_modLookup[modId] = result;
			}
			return result;
		}

		/// <summary>
		/// Creates a snapshot of all optimization option states.
		/// </summary>
		/// <remarks>
		/// The snapshot is used by <see cref="AnyOptionChanged"/> to detect when settings
		/// have been modified and patches need to be reapplied. This should be called
		/// after patches are applied.
		/// </remarks>
		/// <seealso cref="AnyOptionChanged"/>
		public static void CreateOptionSnapshot()
		{
			foreach (var option in Settings.AllOptimizations)
			{
				if ((option.Flags & OptimizationFlags.NoSnapshot) == 0)
				{
					_optionSnapshot[option] = option._enabled;
				}
			}
			IsIdleThrottleEnabled = Settings.OptIdleThrottle.Enabled;
		}

		/// <summary>
		/// Marks the current thread as the Unity main thread during startup.
		/// </summary>
		internal static void MarkAsMainThread()
		{
			if (!UnityData.IsInMainThread)
			{
				YaOptMod.Error($"Thread {Thread.CurrentThread.Name} is not the Unity main thread, " +
							   $"but MarkAsMainThread was called within it.");
			}
			_isInMainThread = true;
		}

		/// <summary>
		/// Checks whether any optimization option has changed since the last snapshot.
		/// </summary>
		/// <remarks>
		/// Used to determine whether patches need to be reapplied when loading a save game.
		/// </remarks>
		/// <seealso cref="CreateOptionSnapshot"/>
		public static bool AnyOptionChanged()
		{
			foreach (var pair in _optionSnapshot)
			{
				if (pair.Key._enabled != pair.Value)
					return true;
			}
			return false;
		}
	}
}