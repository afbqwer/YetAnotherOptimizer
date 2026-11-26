using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;
using YaOpt.Defines;
using YaOpt.Settings;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Detects foreign patches on the methods that the cached, replaced and memoised generation
	/// optimizations rely on, so they can default to off while staying enableable.
	/// </summary>
	/// <remarks>
	/// The guards used to live in each patch's <c>Prepare</c>. They only ran when the patch set was
	/// rebuilt and silently skipped the optimization otherwise. Moving the detection here lets the
	/// settings show a consistent reason and lets the player enable the option anyway. The patches'
	/// <c>Prepare</c> now only honors the option state.
	/// </remarks>
	internal static class PatchConflictDetector
	{
		private const string HarModId = "erdelf.HumanoidAlienRaces";

		internal static void Refresh()
		{
			HarTraitCandidates.Configure();
			CheckPawnGearSessionCache();
			CheckGenerationDefCache();
			CheckPawnGearHarFastPath();
			CheckPawnApparelPairCache();
			CheckPawnBackstoryMatchCache();
			CheckPawnTraitsHarFastPath();
			CheckSoundOneShotManager();
			CheckFleckCreateInPlace();
			CheckFleckTickLoop();
			CheckDrugPolicyCache();
			CheckDrugScheduleCache();
			CheckPrioritySorterSnapshot();
		}

		/// <summary>
		/// The session cache and the pawn gear rebuild both answer the same apparel lookups. When the
		/// rebuild owns the methods the session cache patches are not installed, so there is nothing to
		/// detect.
		/// </summary>
		private static void CheckPawnGearSessionCache()
		{
			if (PawnGearRebuildCompat.Ready)
				return;

			Check(nameof(YaOptSettings.OptPawnGearSessionCache), includeAll: true,
				AccessTools.Method(typeof(ApparelUtility), nameof(ApparelUtility.HasPartsToWear)),
				AccessTools.Method(typeof(PawnApparelGenerator), "CanUseStuff"),
				AccessTools.Method(typeof(PawnWeaponGenerator), "GetCommonality"),
				AccessTools.Method(typeof(GenTemperature), nameof(GenTemperature.SafeTemperatureRange),
					new[] { typeof(Pawn), typeof(List<ThingStuffPair>) }));
		}

		private static void CheckGenerationDefCache()
		{
			Check(nameof(YaOptSettings.OptGenerationDefCache), includeAll: false,
				AccessTools.Method(typeof(GenStuff), nameof(GenStuff.AllowedStuffsFor)),
				AccessTools.Method(typeof(StatWorker_MarketValue), nameof(StatWorker_MarketValue.CalculableRecipe)),
				AccessTools.Method(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.IsDerpWeapon)));
		}

		private static void CheckPawnGearHarFastPath()
		{
			if (!YaOptGlobal.HasMod(HarModId))
				return;

			var cachedData = AccessTools.TypeByName("AlienRace.CachedData");
			var har = AccessTools.TypeByName("AlienRace.HarmonyPatches");
			Check(nameof(YaOptSettings.OptPawnGearHarFastPath), includeAll: false,
				cachedData == null
					? null
					: AccessTools.Method(cachedData, "GetApparelFromApparelProps",
						new[] { typeof(ApparelProperties) }),
				har == null ? null : AccessTools.Method(har, "PawnCanWearPostfix"));
		}

		private static void CheckPawnApparelPairCache()
		{
			Check(nameof(YaOptSettings.OptPawnApparelPairCache), includeAll: false,
				AccessTools.Method(typeof(PawnApparelGenerator), "CanUsePair"));
		}

		private static void CheckPawnBackstoryMatchCache()
		{
			Check(nameof(YaOptSettings.OptPawnBackstoryMatchCache), includeAll: false,
				AccessTools.Method(typeof(BackstoryCategoryFilter), nameof(BackstoryCategoryFilter.Matches),
					new[] { typeof(BackstoryDef) }));
		}

		private static void CheckPawnTraitsHarFastPath()
		{
			if (!YaOptGlobal.HasMod(HarModId))
				return;

			var har = AccessTools.TypeByName("AlienRace.HarmonyPatches");
			Check(nameof(YaOptSettings.OptPawnTraitsHarFastPath), includeAll: false,
				har == null
					? null
					: AccessTools.Method(har, "GenerateTraitsValidator",
						new[] { typeof(List<TraitDef>), typeof(Pawn) }));
		}

		private static void CheckSoundOneShotManager()
		{
			Check(nameof(YaOptSettings.OptSoundOneShotManager), includeAll: false,
				AccessTools.Method(typeof(SampleOneShotManager), nameof(SampleOneShotManager.CanAddPlayingOneShot)),
				AccessTools.Method(typeof(SampleOneShotManager), nameof(SampleOneShotManager.SampleOneShotManagerUpdate)),
				AccessTools.Method(typeof(SampleOneShotManager), nameof(SampleOneShotManager.TryAddPlayingOneShot)));
		}

		private static void CheckFleckCreateInPlace()
		{
			Check(nameof(YaOptSettings.OptFleckCreateInPlace), includeAll: false,
				AccessTools.Method(typeof(FleckManager), nameof(FleckManager.CreateFleck)),
				AccessTools.Method(typeof(FleckSystemStatic), nameof(FleckSystem.CreateFleck)),
				AccessTools.Method(typeof(FleckSystemThrown), nameof(FleckSystem.CreateFleck)),
				AccessTools.Method(typeof(FleckSystemSplash), nameof(FleckSystem.CreateFleck)));
		}

		private static void CheckFleckTickLoop()
		{
			Check(nameof(YaOptSettings.OptFleckTickLoop), includeAll: false,
				AccessTools.Method(typeof(FleckManager), nameof(FleckManager.FleckManagerTick)),
				AccessTools.Method(typeof(FleckSystemStatic), nameof(FleckSystem.Tick)),
				AccessTools.Method(typeof(FleckSystemThrown), nameof(FleckSystem.Tick)),
				AccessTools.Method(typeof(FleckSystemSplash), nameof(FleckSystem.Tick)));
		}

		/// <summary>
		/// The drug policy index and the priority memo both short-circuit the original on a hit, so a
		/// foreign transpiler or short-circuiting prefix would be skipped. Plain instrumentation
		/// prefixes and postfixes coexist and are not treated as conflicts.
		/// </summary>
		private static void CheckDrugPolicyCache()
		{
			Check(nameof(YaOptSettings.OptDrugPolicyCache), includeAll: false,
				AccessTools.Method(typeof(DrugPolicy), "get_Item", new[] { typeof(ThingDef) }),
				AccessTools.Method(typeof(JobGiver_TakeDrugsForDrugPolicy),
					nameof(JobGiver_TakeDrugsForDrugPolicy.GetPriority)),
				AccessTools.Method(typeof(JobGiver_MoveDrugsToInventory),
					nameof(JobGiver_MoveDrugsToInventory.GetPriority)));
		}

		/// <summary>
		/// The scheduled-drug result cache short-circuits the tracker method on a hit, so a foreign
		/// transpiler or short-circuiting prefix would be skipped.
		/// </summary>
		private static void CheckDrugScheduleCache()
		{
			Check(nameof(YaOptSettings.OptDrugScheduleCache), includeAll: false,
				AccessTools.Method(typeof(Pawn_DrugPolicyTracker),
					nameof(Pawn_DrugPolicyTracker.ShouldTryToTakeScheduledNow)));
		}

		/// <summary>
		/// The priority snapshot replaces the sorter body, so any transpiler on it would be bypassed.
		/// </summary>
		private static void CheckPrioritySorterSnapshot()
		{
			Check(nameof(YaOptSettings.OptPrioritySorterSnapshot), includeAll: false,
				AccessTools.Method(typeof(ThinkNode_PrioritySorter),
					nameof(ThinkNode_PrioritySorter.TryIssueJobPackage)));
		}

		private static void Check(string settingId, bool includeAll, params MethodBase[] targets)
		{
			var reasons = new List<string>();
			foreach (var target in targets)
			{
				if (target == null)
				{
					// A missing vanilla method cannot be replaced safely; treat it as a conflict.
					reasons.Add("目标方法缺失：" + settingId);
					continue;
				}

				PatchConflictGuard.CollectForeignConflicts(target, includeAll, reasons);
			}

			if (reasons.Count > 0)
				CompatibilityDefines.PatchConflict(settingId, reasons);
		}
	}
}