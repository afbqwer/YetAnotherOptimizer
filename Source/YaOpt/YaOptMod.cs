using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;
using YaOpt.Helpers;
using YaOpt.Patches;
using YaOpt.Prepatch;

namespace YaOpt
{
	/// <summary>
	/// Main entry point for the YetAnotherOptimizer mod.
	/// </summary>
	public class YaOptMod : Mod
	{
		/// <summary>
		/// Gets the singleton instance of the mod.
		/// </summary>
		public static YaOptMod Instance { get; private set; }

		/// <summary>
		/// Gets the main Harmony instance used for patching.
		/// </summary>
		/// <remarks>
		/// This Harmony instance has ID "YetAnotherOptimizer" and is used for all standard patches.
		/// </remarks>
		public Harmony Harmony { get; } = new Harmony("YetAnotherOptimizer");

		/// <summary>
		/// Gets the Harmony instance for early patches that must be applied before other mods load.
		/// </summary>
		/// <remarks>
		/// This Harmony instance has ID "YetAnotherOptimizer.Early" and is used for patches
		/// that need to modify game behavior during the loading phase.
		/// </remarks>
		public Harmony EarlyHarmony { get; } = new Harmony("YetAnotherOptimizer.Early");

		/// <summary>
		/// Gets the mod settings containing all optimization options.
		/// </summary>
		public YaOptSettings Settings { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="YaOptMod"/> class.
		/// </summary>
		public YaOptMod(ModContentPack content) : base(content)
		{
			// Set the singleton instance and detect if Multiplayer mod is active.
			Instance = this;
			Log("Now loading...");
			YaOptGlobal.IsMultiplayer = YaOptGlobal.HasMod("rwmt.Multiplayer");

			// Load settings.
			Settings = GetSettings<YaOptSettings>();
			YaOptGlobal.IsLibraryLoaded = true;

			// Fix compatibility with Hyperdrive mod.
			CompatibilityChecker.CheckHyperdrive();

			// Apply settings and early Harmony patches.
			ApplySettings();
			ApplyEarlyPatches();
		}

		/// <summary>
		/// Validates and applies the current settings.
		/// </summary>
		private void ApplySettings()
		{
			Settings.ValidateOptions();
			PrepatcherState.Refresh();

#if false && DEBUG
			HarmonyLib.Harmony.DEBUG = true;
#endif
		}

		/// <summary>
		/// Applies early Harmony patches that must run before other mods load.
		/// </summary>
		private void ApplyEarlyPatches()
		{
			var noError = true;
			try
			{
				noError &= EarlyHarmony.TryPatchAll(Assembly.GetExecutingAssembly(), true);
			}
			catch (Exception ex)
			{
				noError = false;
				Error(ex.ToString());
			}

			if (!noError)
			{
				Panic("Error(s) happened while early patching. The game may not run properly.");
				Messages.Message("YaOpt.Message.ErrorWhileEarlyPatching".Translate().ToString(),
					null, MessageTypeDefOf.CautionInput, false);
			}
		}

		/// <summary>
		/// Draws the settings window UI.
		/// </summary>
		public override void DoSettingsWindowContents(Rect inRect)
		{
			Settings.DoSettingsWindowContents(inRect);
		}

		/// <summary>
		/// Gets the display name for the settings category.
		/// </summary>
		public override string SettingsCategory()
		{
			return "YaOpt";
		}

		/// <summary>
		/// Clears all cached data and triggers re-patching on game load.
		/// </summary>
		/// <remarks>
		/// Called when loading a save game to ensure caches are fresh and patches are up-to-date.
		/// </remarks>
		public void ClearCaches()
		{
			UpdateCallbackHelper.ClearCache();
			Patcher.CheckAndRePatchOnLoadGame();
			if (PlayDataLoader.Loaded)
				DefLookupCacheHelper.BuildAll("game load");
		}

		/// <summary>
		/// Logs a debug message to the RimWorld console with [YaOpt] prefix if debug logging is enabled.
		/// </summary>
		public static void Debug(string message)
		{
			if (Instance?.Settings?.DebugLogging == true)
				Verse.Log.Message("[YaOpt] " + message);
		}

		/// <summary>
		/// Logs an informational message to the RimWorld console with [YaOpt] prefix.
		/// </summary>
		public static void Log(string message)
		{
			Verse.Log.Message("[YaOpt] " + message);
		}

		/// <summary>
		/// Logs a yellow warning message to the RimWorld console with [YaOpt] prefix.
		/// </summary>
		public static void Warning(string message)
		{
			Verse.Log.Warning("[YaOpt] " + message);
		}

		/// <summary>
		/// Logs a red error message to the RimWorld console with [YaOpt] prefix.
		/// </summary>
		public static void Error(string message)
		{
			Verse.Log.Error("[YaOpt] " + message);
		}

		/// <summary>
		/// Logs a red error message to the RimWorld console, but only once per unique key.
		/// </summary>
		public static void ErrorOnce(string message, int key)
		{
			Verse.Log.ErrorOnce("[YaOpt] " + message, key);
		}

		/// <summary>
		/// Handles critical errors by logging the error, pausing the game, and forcing the log window to open.
		/// </summary>
		internal static void Panic(string message)
		{
			Verse.Log.Error("[YaOpt Critical!!] " + message);

			if (Current.ProgramState == ProgramState.Playing)
			{
				Find.TickManager.Pause();
			}

			LudeonTK.EditWindow_Log.wantsToOpen = true;
		}
	}
}
