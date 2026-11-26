using RimWorld;
using System;
using System.Reflection;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Manages the application and removal of Harmony patches for the mod.
	/// </summary>
	internal static class Patcher
	{
		/// <summary>
		/// Tracks whether patches have been applied at least once.
		/// </summary>
		private static bool _hasPatched;

		/// <summary>
		/// Initializes the patcher and queues patching as a long event.
		/// This method is called during game startup to apply patches with a loading screen.
		/// </summary>
		public static void Init()
		{
			LongEventHandler.QueueLongEvent(PatchOnStartup, "YaOpt.Loading.Patching".Translate(), false, null);
		}

		/// <summary>
		/// Applies patches during the startup loading phase.
		/// </summary>
		private static void PatchOnStartup()
		{
			WorldMapOptimizationHelper.CheckConflicts();
			DefLookupCacheHelper.CheckConflicts();
			TryPatch(true, false);
			CompatibilityChecker.Check(false);
		}

		/// <summary>
		/// Checks for setting changes and triggers re-patching when loading a game.
		/// Called from <see cref="YaOptMod.ClearCaches"/> when a save game is loaded.
		/// </summary>
		public static void CheckAndRePatchOnLoadGame()
		{
			WorldMapOptimizationHelper.CheckConflicts();
			DefLookupCacheHelper.CheckConflicts();
			TryPatch(false, true);
			CompatibilityChecker.Check(true);
		}

		/// <summary>
		/// Applies or reapplies all Harmony patches based on current settings. 
		/// If any errors occur during patching, a warning message is displayed to the player.
		/// </summary>
		public static void TryPatch(bool force, bool changeLongEventTitle, bool updateOptionSnapshot = true)
		{
			PrepatcherState.Refresh();
			var gridCompatibilityChanged = GridOptimizationCompat.Refresh();
			GenerationOptimizationCompat.Refresh();
			PawnGearRebuildCompat.Refresh();
			if (!force && !gridCompatibilityChanged && !YaOptGlobal.AnyOptionChanged())
				return;

			if (changeLongEventTitle)
			{
				LongEventHandler.SetCurrentEventText("YaOpt.Loading.Patching".Translate());
			}

			var noError = true;
			try
			{
				YaOptMod.Debug("Preparing to run patcher");
				var assembly = Assembly.GetExecutingAssembly();
				var harmony = YaOptMod.Instance.Harmony;

				// Uninstall existing patches if already patched.
				if (_hasPatched)
				{
					YaOptMod.Debug("Uninstalling exist patches...");
					harmony.UnpatchAll(harmony.Id);
					_hasPatched = false;
				}

				// Decide whether the thing index can be maintained at all before any patch that
				// depends on it is applied; Prepare() of those patches reads the result and their
				// evaluation order is not something we control.
				ListerThingsHelper.EvaluateFastRemoveAvailability();

				// Apply Harmony patches from the current assembly.
				YaOptMod.Debug("Patching...");
				var corePatchesSucceeded = harmony.TryPatchAll(assembly);
				noError &= corePatchesSucceeded;
				if (corePatchesSucceeded)
				{
					// Caches may have been populated before their maintenance patches existed.
					ListerBuildingsCache.Invalidate();
					RefuelWorkCandidates.Invalidate();
				}
				_hasPatched = true;
				YaOptMod.Debug("Patcher complated");
			}
			catch (Exception ex)
			{
				YaOptMod.Error(ex.ToString());
				noError = false;
			}
			if (!noError)
			{
				YaOptMod.Panic("Error(s) happened while patching. The game may not run properly.");
				Messages.Message("YaOpt.Message.ErrorWhilePatching".Translate().ToString(),
					null, MessageTypeDefOf.CautionInput, false);
			}

			if (updateOptionSnapshot)
				YaOptGlobal.CreateOptionSnapshot();
		}
	}
}