using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using LudeonTK;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PathFinderMapData.GatherData"/> to deduplicate the expanded cell deltas with
	/// per-cell stamps and to gather small deltas serially.
	/// </summary>
	/// <remarks>
	/// Vanilla expands every queued rect into <c>cellDeltas</c> through a <c>HashSet&lt;IntVec3&gt;</c> and fans
	/// the source update out to the thread pool even for a handful of cells. This keeps vanilla's recycle
	/// semantics (any delta or source change forces a full grid rebuild) and only changes how the deltas are
	/// collected. The vanilla profiler scopes are kept so existing profiles remain comparable.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathGatherFast"/>
	[HarmonyPatch(typeof(PathFinderMapData))]
	[HarmonyPatch(nameof(PathFinderMapData.GatherData))]
	internal static class Verse_PathFinderMapData_GatherData
	{
		private const int SerialThreshold = 512;

		private static readonly ConditionalWeakTable<PathFinderMapData, CellDeltaStamp> _stamps =
			new ConditionalWeakTable<PathFinderMapData, CellDeltaStamp>();

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathGatherFast.Enabled;
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

			var indices = mapData.map.cellIndices;
			var stamp = _stamps.GetOrCreateValue(mapData);
			stamp.Begin(indices.NumGridCells);

			// Cells queued directly by Notify_CellDelta(IntVec3) are already in cellDeltas; mark them first
			// so the rect expansion below does not add them a second time.
			for (var i = 0; i < mapData.cellDeltas.Count; i++)
				stamp.Mark(indices.CellToIndex(mapData.cellDeltas[i]));

			foreach (var rect in mapData.cellRectDeltas)
			{
				foreach (var cell in rect.Cells)
				{
					if (!cell.InBounds(mapData.map))
						continue;
					if (stamp.Mark(indices.CellToIndex(cell)))
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

			var recycle = mapData.cellDeltas.Count > 0 || sourceChanged;

			mapData.cellDeltas.Clear();
			mapData.cellDeltaSet.Clear();
			mapData.lastGatherTick = GenTicks.TicksGame;
			return recycle;
		}
	}
}