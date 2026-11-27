using System.Collections.Generic;
using RimWorld;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Drop-in replacement for <see cref="PathGridDoorsBlockedJob.Execute"/> that clears only the cells the
	/// previous run on the same overlay buffers wrote, instead of clearing the two whole-map arrays
	/// (<c>ushort</c> + <c>bool</c>, ~187 KB on a 250x250 map) for every path.
	/// </summary>
	/// <remarks>
	/// Doors, cost providers and blocking pawns touch a handful of cells, so the full clear dominates the
	/// job. The body is transcribed from vanilla; the only change is that every cell written is recorded so
	/// the next run can zero exactly those cells. Buffers are pooled and reused, so the recorded set always
	/// describes the current contents.
	/// </remarks>
	internal static class OptimizedDoorsJob
	{
		private const int MinDistToCheckBlocked = 32;

		internal static void Execute(PathGridDoorsBlockedJob job)
		{
			var providerCost = job.providerCost;
			var blocked = job.blocked;
			var touch = ScratchTouchRegistry.Get(providerCost, out var fullClear);
			if (fullClear)
			{
				for (var i = 0; i < providerCost.Length; i++)
					providerCost[i] = 0;
				for (var i = 0; i < blocked.Length; i++)
					blocked[i] = false;
			}
			else
			{
				var previous = touch.Indices;
				for (var i = 0; i < previous.Count; i++)
				{
					var index = previous[i];
					providerCost[index] = 0;
					blocked[index] = false;
				}
			}
			var touched = touch.Indices;
			touched.Clear();

			var map = job.map;
			var cellIndices = map.cellIndices;
			var doors = job.doors;
			var pawn = job.pawn;

			if (doors != null)
			{
				for (var d = 0; d < doors.Count; d++)
				{
					if (!(doors[d] is Building_Door door))
						continue;
					foreach (var cell in door.OccupiedRect())
					{
						var index = cellIndices.CellToIndex(cell);
						providerCost[index] = PathUtility.GetDoorCost(door, job.traverseParams, pawn, job.tuning);
						touched.Add(index);
					}
				}
			}

			var providers = job.providers;
			if (pawn != null && providers != null)
			{
				for (var p = 0; p < providers.Count; p++)
				{
					var provider = providers[p];
					foreach (var cell in provider.GetOccupiedRect())
					{
						var index = cellIndices.CellToIndex(cell);
						providerCost[index] += provider.PathFindCostFor(pawn);
						touched.Add(index);
					}
				}
			}

			if (!PawnUtility.ShouldCollideWithPawns(pawn))
				return;

			var pawns = job.pawns;
			if (pawns == null)
				return;

			var collideWithNonHostile = pawn.CurJob != null &&
				(pawn.CurJob.collideWithPawns || pawn.CurJob.def.collideWithPawns || pawn.jobs.curDriver.collideWithPawns);
			for (var i = 0; i < pawns.Count; i++)
			{
				var other = pawns[i];
				if (other == pawn || !CanBlockEver(job, other, cellIndices, blocked))
					continue;
				if (PawnUtility.PawnBlockedBy(pawn, other, collideOnlyWithStandingPawns: false, collideWithNonHostile, forPathFinder: true))
				{
					var index = cellIndices.CellToIndex(other.Position);
					blocked[index] = true;
					touched.Add(index);
				}
			}
		}

		private static bool CanBlockEver(PathGridDoorsBlockedJob job, Pawn other, CellIndices cellIndices,
			NativeArray<bool> blocked)
		{
			if (other.Destroyed || other.Discarded || !other.Spawned)
				return false;
			if (blocked[cellIndices.CellToIndex(other.Position)])
				return false;
			if (!job.pawn.pather.BestPathHadPawnsInTheWayRecently())
			{
				var toStart = (other.Position - job.start).LengthManhattan;
				var toDest = (other.Position - job.dest).LengthManhattan;
				if (Mathf.Min(toStart, toDest) > MinDistToCheckBlocked)
					return false;
			}
			return PawnUtility.ShouldCollideWithPawns(other);
		}
	}
}