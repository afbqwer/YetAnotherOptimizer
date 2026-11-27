using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using LudeonTK;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PathFinderMapData.GatherData"/> to serialise small deltas and to patch cached
	/// grids in place instead of reporting "changed" (which makes the caller discard every grid).
	/// </summary>
	/// <remarks>
	/// Vanilla seeds <c>anyChanged</c> with <c>cellDeltas.Any()</c>, so a reservation or a haul-enroute on an
	/// empty cell — which no source reads — still forces a full-map grid rebuild for every key. The
	/// per-cell sources only write the changed cells and always return <c>false</c>; the only sources that
	/// return <c>true</c> (areas, unnatural darkness) are structural and do need a full rebuild.
	/// <para/>
	/// This replacement runs the sources (serially for small deltas, keeping the TPL fan-out only for large
	/// ones) and then, when nothing structural changed, patches every cached patchable grid for the changed
	/// cells. It returns <c>true</c> only when a full recycle is genuinely required, so callers behave as
	/// before. If the template-store patch is not present, it falls back to vanilla's "any delta -> recycle".
	/// The vanilla profiler scopes are kept so existing profiles remain comparable.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathIncrementalGrid"/>
	[HarmonyPatch(typeof(PathFinderMapData))]
	[HarmonyPatch(nameof(PathFinderMapData.GatherData))]
	internal static class Verse_PathFinderMapData_GatherData
	{
		private const int SerialThreshold = 512;

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathIncrementalGrid.Enabled;
		}

		static bool Prefix(PathFinderMapData __instance, IEnumerable<PathRequest> requests, ref bool __result)
		{
			using (ProfilerBlock.Scope("PathFinderMapData.GatherData"))
			{
				__result = GatherData(__instance, requests);
			}
			return false;
		}

		private static bool GatherData(PathFinderMapData mapData, IEnumerable<PathRequest> requests)
		{
			if (mapData.lastGatherTick == GenTicks.TicksGame)
				return false;

			foreach (var rect in mapData.cellRectDeltas)
			{
				foreach (var cell in rect.Cells)
				{
					if (cell.InBounds(mapData.map) && mapData.cellDeltaSet.Add(cell))
						mapData.cellDeltas.Add(cell);
				}
			}
			mapData.cellRectDeltas.Clear();

			var sourceChanged = false;
			if (mapData.lastGatherTick >= 0)
			{
				using (ProfilerBlock.Scope("incremental"))
				{
					if (mapData.cellDeltas.Count <= SerialThreshold)
					{
						for (var i = 0; i < mapData.sources.Count; i++)
						{
							if (mapData.sources[i].UpdateIncrementally(requests, mapData.cellDeltas))
								sourceChanged = true;
						}
					}
					else
					{
						Parallel.ForEach(mapData.sources, source =>
						{
							if (source.UpdateIncrementally(requests, mapData.cellDeltas))
								sourceChanged = true;
						});
					}
				}
			}
			else
			{
				sourceChanged = true;
				using (ProfilerBlock.Scope("recompute"))
				{
					// First gather (map load / after fog change): keep vanilla's parallel full recompute.
					Parallel.ForEach(mapData.sources, source => source.ComputeAll(requests));
				}
			}

			bool recycle;
			if (PathGridIncremental.TemplatesReady)
			{
				var pathFinder = mapData.map.pathFinder;
				var forceFull = PathGridIncremental.ConsumeForceFullRecycle(pathFinder);
				if (forceFull || sourceChanged)
					recycle = true;
				else if (mapData.cellDeltas.Count == 0)
					recycle = false;
				else
					recycle = !PathGridIncremental.TryPatch(pathFinder, mapData.cellDeltas);
			}
			else
			{
				// Vanilla semantics: any delta (or any source-reported change) discards all grids.
				recycle = mapData.cellDeltas.Count > 0 || sourceChanged;
			}

			mapData.cellDeltas.Clear();
			mapData.cellDeltaSet.Clear();
			mapData.lastGatherTick = GenTicks.TicksGame;
			return recycle;
		}
	}
}