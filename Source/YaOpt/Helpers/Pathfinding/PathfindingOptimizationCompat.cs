using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;
using YaOpt.Defines;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Turns the pathfinding optimizations off by default when another mod patches the methods they replace.
	/// </summary>
	/// <remarks>
	/// These patches replace whole vanilla methods (gather, schedule, both job bodies) and skip the
	/// <c>ForceCompleteScheduledJobs</c> call inside <c>FindPathNow</c>. A third-party patch on the same
	/// members would be skipped or fed stale data, so falling back to vanilla is safer. Runs before patching
	/// on every re-patch so each patch's <c>Prepare</c> sees the resulting default.
	/// </remarks>
	internal static class PathfindingOptimizationCompat
	{
		internal static bool Refresh()
		{
			var changed = false;
			changed |= Check(nameof(YaOptSettings.OptPathIncrementalGrid), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinderMapData), nameof(PathFinderMapData.GatherData)),
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.ScheduleGridJob)),
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.RecycleGridJobData)),
				AccessTools.Method(typeof(PathFinderMapData), nameof(PathFinderMapData.Notify_FactionRemoved))
			});
			changed |= Check(nameof(YaOptSettings.OptPathGatherFast), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinderMapData), nameof(PathFinderMapData.GatherData))
			});
			changed |= Check(nameof(YaOptSettings.OptPathGridOutputUninit), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.GetOrCreateGridJobOutput))
			});
			changed |= Check(nameof(YaOptSettings.OptPathDoorsJobFast), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.ScheduleBatchedPathJobs)),
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.GetDoorBlockedJob)),
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.EnsureDoorsPawnsCached)),
				AccessTools.Method(typeof(PathGridDoorsBlockedJob), nameof(PathGridDoorsBlockedJob.Execute)),
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.FindPathNow), new[]
				{
					typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms),
					typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer)
				})
			});
			changed |= Check(nameof(YaOptSettings.OptPathJobCompletion), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.ForceCompleteScheduledJobs))
			});
			changed |= Check(nameof(YaOptSettings.OptPathMoveTicksCache), new MethodBase[]
			{
				AccessTools.Method(typeof(Pawn), nameof(Pawn.TicksPerMove))
			});
			changed |= Check(nameof(YaOptSettings.OptPathNodeReset), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinderJob), nameof(PathFinderJob.Execute))
			});
			changed |= Check(nameof(YaOptSettings.OptPathOverlaySparse), new MethodBase[]
			{
				AccessTools.Method(typeof(PathGridDoorsBlockedJob), nameof(PathGridDoorsBlockedJob.Execute))
			});
			changed |= Check(nameof(YaOptSettings.OptPathFindNowNoBarrier), new MethodBase[]
			{
				AccessTools.Method(typeof(PathFinder), nameof(PathFinder.FindPathNow), new[]
				{
					typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms),
					typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer)
				})
			});
			return changed;
		}

		private static bool Check(string settingId, MethodBase[] targets)
		{
			var reasons = new List<string>();
			foreach (var target in targets)
			{
				if (target == null)
					continue;
				PatchConflictGuard.CollectForeignConflicts(target, includeAll: false, reasons);
			}

			if (reasons.Count == 0)
				return false;

			var changed = CompatibilityDefines.PatchConflict(settingId, reasons);
			if (changed)
				YaOptMod.Warning($"{settingId} disabled by default: {string.Join(", ", reasons)}");
			return changed;
		}
	}
}