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
			CheckGravshipJobFastExit();
			CheckOxygenJobFastExit();
			CheckShamblerStateCache();
			CheckRegionQueryFast();
			CheckRegionProcessorDistFilter();
			CheckBeautyFastPath();
			CheckTempTerrainMerge();
			CheckPawnComputeMatrix();
			CheckPawnRenderPreparation();
			CheckEarlyRenderPrepare();
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
			CheckReplacement(nameof(YaOptSettings.OptPawnGearHarFastPath),
				cachedData == null
					? null
					: AccessTools.Method(cachedData, "GetApparelFromApparelProps",
						new[] { typeof(ApparelProperties) }),
				har == null ? null : AccessTools.Method(har, "PawnCanWearPostfix"));
		}

		private static void CheckPawnApparelPairCache()
		{
			CheckReplacement(nameof(YaOptSettings.OptPawnApparelPairCache),
				AccessTools.Method(typeof(PawnApparelGenerator), "CanUsePair"));
		}

		private static void CheckPawnBackstoryMatchCache()
		{
			CheckReplacement(nameof(YaOptSettings.OptPawnBackstoryMatchCache),
				AccessTools.Method(typeof(BackstoryCategoryFilter), nameof(BackstoryCategoryFilter.Matches),
					new[] { typeof(BackstoryDef) }));
		}

		private static void CheckPawnTraitsHarFastPath()
		{
			if (!YaOptGlobal.HasMod(HarModId))
				return;

			var har = AccessTools.TypeByName("AlienRace.HarmonyPatches");
			CheckReplacement(nameof(YaOptSettings.OptPawnTraitsHarFastPath),
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

		private static void CheckGravshipJobFastExit()
		{
			if (!ModsConfig.OdysseyActive)
				return;

			var target = AccessTools.Method(typeof(JobGiver_BoardOrLeaveGravship), "TryGiveJob");
			var reasons = new List<string>();
			if (target == null)
			{
				reasons.Add("目标方法缺失：" + nameof(YaOptSettings.OptGravshipJobFastExit));
			}
			else
			{
				PatchConflictGuard.CollectForeignConflicts(target, includeAll: false, reasons,
					IsCompatibleVveGravshipPatch);
			}

			if (reasons.Count > 0)
				CompatibilityDefines.PatchConflict(nameof(YaOptSettings.OptGravshipJobFastExit), reasons);
		}

		private static bool IsCompatibleVveGravshipPatch(MethodBase target, Patch patch)
		{
			// VVE only replaces the engine-list provider; FirstOrDefault and job control flow stay intact.
			return target.Name == "TryGiveJob" &&
				patch.owner == "vanillaexpanded.gravship" &&
				patch.PatchMethod?.DeclaringType?.FullName ==
					"VanillaGravshipExpanded.JobGiver_BoardOrLeaveGravship_TryGiveJob_Patch" &&
				patch.PatchMethod.Name == "Transpiler";
		}

		private static void CheckOxygenJobFastExit()
		{
			if (!ModsConfig.OdysseyActive)
				return;

			Check(nameof(YaOptSettings.OptOxygenJobFastExit), includeAll: false,
				AccessTools.Method(typeof(JobGiver_FindOxygen), "TryGiveJob"));
		}

		private static void CheckShamblerStateCache()
		{
			if (!ModsConfig.AnomalyActive)
				return;

			Check(nameof(YaOptSettings.OptShamblerStateCache), includeAll: true,
				AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsShambler)));
		}

		/// <summary>
		/// The region fast path skips vanilla's rebuild check, and <c>RegionAt</c> bypasses
		/// <c>GetValidRegionAt</c> entirely, so any foreign patch on either method is bypassed.
		/// </summary>
		private static void CheckRegionQueryFast()
		{
			Check(nameof(YaOptSettings.OptRegionQueryFast), includeAll: true,
				AccessTools.Method(typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.RegionAt)),
				AccessTools.Method(typeof(RegionGrid), nameof(RegionGrid.GetValidRegionAt)));
		}

		/// <summary>
		/// The distance-first guard skips reachability calls for things that vanilla cannot select,
		/// so a foreign prefix or transpiler on that method would not see those calls. A foreign
		/// transpiler on <c>ProcessThing</c> itself can also reshape the body past our IL check.
		/// </summary>
		private static void CheckRegionProcessorDistFilter()
		{
			Check(nameof(YaOptSettings.OptRegionProcessorDistFilter), includeAll: false,
				AccessTools.Method(typeof(ReachabilityWithinRegion),
					nameof(ReachabilityWithinRegion.ThingFromRegionListerReachable)),
				AccessTools.Method(typeof(RegionProcessorClosestThingReachable), "ProcessThing"));
		}

		/// <summary>
		/// The beauty fast path replaces the body of <c>AverageBeautyPerceptible</c> outright, so any
		/// foreign patch on it - prefix, postfix or transpiler - would be bypassed.
		/// </summary>
		private static void CheckBeautyFastPath()
		{
			Check(nameof(YaOptSettings.OptBeautyFastPath), includeAll: true,
				AccessTools.Method(typeof(BeautyUtility), nameof(BeautyUtility.AverageBeautyPerceptible)));
		}

		/// <summary>
		/// The merged temporary-terrain pass replaces <c>DoCellSteadyEffects</c> and therefore does not
		/// call the two private passes, so any foreign patch on them is bypassed.
		/// </summary>
		private static void CheckTempTerrainMerge()
		{
			if (!ModsConfig.OdysseyActive)
				return;

			Check(nameof(YaOptSettings.OptTempTerrainMerge), includeAll: true,
				AccessTools.Method(typeof(FreezeManager),
					nameof(FreezeManager.DoCellSteadyEffects)),
				AccessTools.Method(typeof(FreezeManager), "DoWaterFreezing"),
				AccessTools.Method(typeof(FreezeManager), "DoIceMelting"),
				AccessTools.Method(typeof(FreezeManager), "CanFreeze"),
				AccessTools.Method(typeof(FreezeManager), "CanMelt"),
				AccessTools.Method(typeof(FreezeManager), nameof(FreezeManager.CanEverFreeze)),
				AccessTools.Method(typeof(FreezeManager), nameof(FreezeManager.CanEverMelt)),
				AccessTools.Method(typeof(FreezeManager), "SurroundedByIce"),
				AccessTools.Method(typeof(FreezeManager), "SurroundedByWater"));
		}

		/// <summary>
		/// The matrix replacement swaps the <c>ComputeMatrix</c> call site, so any foreign patch on
		/// that method is bypassed entirely, while only transpilers and short-circuiting prefixes on
		/// <c>TryGetMatrix</c> would fight over the call.
		/// </summary>
		private static void CheckPawnComputeMatrix()
		{
			Check(nameof(YaOptSettings.OptComputeMatrixBurst), includeAll: true,
				AccessTools.Method(typeof(PawnRenderTree), "ComputeMatrix"));
			Check(nameof(YaOptSettings.OptComputeMatrixBurst), includeAll: false,
				AccessTools.Method(typeof(PawnRenderTree), nameof(PawnRenderTree.TryGetMatrix)));
		}

		private static void CheckPawnRenderPreparation()
		{
			Check(nameof(YaOptSettings.OptPrepareBatchCount), includeAll: false,
				AccessTools.Method(typeof(DynamicDrawManager), "PreDrawVisibleThings"));
		}

		/// <summary>
		/// The early preparation rewrites the draw method body and defers the job completions, so any
		/// foreign transpiler or short-circuiting prefix on the four targets is incompatible.
		/// </summary>
		private static void CheckEarlyRenderPrepare()
		{
			Check(nameof(YaOptSettings.OptEarlyRenderPrepare), includeAll: false,
				AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings)),
				AccessTools.Method(typeof(DynamicDrawManager), "ComputeCulledThings"),
				AccessTools.Method(typeof(DynamicDrawManager), "PreDrawVisibleThings"),
				AccessTools.Method(typeof(Map), nameof(Map.MapUpdate)));
		}

		private static void CheckReplacement(string settingId, params MethodBase[] targets)
		{
			var reasons = new List<string>();
			foreach (var target in targets)
			{
				if (target == null)
				{
					reasons.Add("目标方法缺失：" + settingId);
					continue;
				}

				PatchConflictGuard.CollectForeignReplacementConflicts(target, reasons);
			}

			if (reasons.Count > 0)
				CompatibilityDefines.PatchConflict(settingId, reasons);
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