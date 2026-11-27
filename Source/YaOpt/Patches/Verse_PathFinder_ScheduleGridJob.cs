using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Captures the parameterized <see cref="PathGridJob"/> for each cached grid key so a later tick can
	/// re-run it for just the changed cells instead of rebuilding the whole grid.
	/// </summary>
	/// <remarks>
	/// Only keys whose grid depends purely on delta-tracked data are stored (no customizer, no
	/// <see cref="Verse.AI.AvoidGrid"/>, no allowed <see cref="Area"/>, no lord walk grid); see
	/// <see cref="PathGridIncremental.IsPatchable"/>. Storing the template keeps the same
	/// <see cref="NativeArray{T}"/> handles the grid was built from, so patching writes the exact values a
	/// rebuild would.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathIncrementalGrid"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.ScheduleGridJob))]
	internal static class Verse_PathFinder_ScheduleGridJob
	{
		static bool Prepare()
		{
			var enabled = YaOptGlobal.Settings.OptPathIncrementalGrid.Enabled;
			PathGridIncremental.Reset();
			PathGridIncremental.TemplatesReady = enabled;
			return enabled;
		}

		static void Postfix(PathFinder __instance, PathRequest request)
		{
			var query = PathFinder.MapGridRequest.For(request);
			PathGridDiagnostics.Scheduled(query, request);
			if (!PathGridIncremental.IsPatchable(query, request))
			{
				PathGridIncremental.Forget(__instance, query);
				return;
			}

			// ScheduleGridJob stores itself into gridJobLookup before returning, so the output grid can be
			// read from there instead of the value-type __result.
			if (!__instance.gridJobLookup.TryGetValue(query, out var scheduled))
				return;

			var job = default(PathGridJob);
			var output = scheduled.output;
			__instance.MapData.ParameterizeGridJob(request, ref query, ref job, ref output);
			PathGridIncremental.Store(__instance, query, job);
		}
	}
}