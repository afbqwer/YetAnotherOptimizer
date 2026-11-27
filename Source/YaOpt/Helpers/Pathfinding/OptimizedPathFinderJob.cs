using System;
using System.Runtime.CompilerServices;
using LudeonTK;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Drop-in replacement for <see cref="PathFinderJob.Execute"/> that avoids the full
	/// <c>calcGrid.Clear()</c> (20 bytes x map cells per path) by resetting only the cells the previous
	/// run on the same batch actually touched.
	/// </summary>
	/// <remarks>
	/// The A* body is transcribed verbatim from vanilla; the only addition is recording a cell index the
	/// first time it leaves the <c>None</c> state, so the next run can reset exactly those cells to
	/// <c>default</c> instead of clearing the whole array. Untouched cells are always <c>default</c>
	/// because every batch array is zero-initialized and the previous run's touched set is restored, so
	/// the state seen by a run is identical to vanilla's post-<c>Clear()</c> state. Path output is thus
	/// byte-for-byte unchanged.
	/// </remarks>
	internal static class OptimizedPathFinderJob
	{
		private const int CostBlocked = 175;
		private const int CostImpassable = 10000;

		internal static void Execute(PathFinderJob job)
		{
			var calcGrid = job.calcGrid;
			var frontier = job.frontier;
			var path = job.path;
			var result = job.result;
			var indices = job.indices;
			var connectivity = job.connectivity;
			var fences = job.fences;
			var buildings = job.buildings;
			var grid = job.grid;
			var providerCost = job.providerCost;
			var blocked = job.blocked;
			var excludedRectIndices = job.excludedRectIndices;
			var traverseParams = job.traverseParams;
			var moveTicksCardinal = job.moveTicksCardinal;
			var moveTicksDiagonal = job.moveTicksDiagonal;
			var start = job.start;
			var destCell = job.destCell;
			var destRect = job.destRect;
			var heuristicStrength = job.heuristicStrength;

			var touch = ScratchTouchRegistry.Get(calcGrid, out var fullClear);
			if (fullClear)
			{
				for (var i = 0; i < calcGrid.Length; i++)
					calcGrid[i] = default;
			}
			else
			{
				var previous = touch.Indices;
				for (var i = 0; i < previous.Count; i++)
					calcGrid[previous[i]] = default;
			}
			var touched = touch.Indices;
			touched.Clear();

			frontier.Clear();
			path.Clear();
			result.Value = default;

			var destIndex = indices.CellToIndex(destCell);
			var singleCellDest = destRect.Area == 1;
			var visitedCount = 0;

			// InitalizeSearch
			var startIndex = indices.CellToIndex(start);
			var startNode = calcGrid[startIndex];
			startNode.parentIndex = startIndex;
			startNode.status = PathFinderJob.CalcNode.Status.Open;
			calcGrid[startIndex] = startNode;
			touched.Add(startIndex);
			frontier.Enqueue(startIndex, 0f);

			while (frontier.Count > 0)
			{
				frontier.Dequeue(out var element, out var priority);
				if (!Mathf.Approximately(priority, calcGrid[element].fCost) ||
					calcGrid[element].status == PathFinderJob.CalcNode.Status.Closed)
				{
					continue;
				}

				var cell = indices.IndexToCell(element);
				if (singleCellDest)
				{
					if (element == destIndex)
					{
						FinalizePath(path, calcGrid, result, indices, element);
						break;
					}
				}
				else if (destRect.Contains(cell) && !excludedRectIndices.Contains(element))
				{
					FinalizePath(path, calcGrid, result, indices, element);
					break;
				}

				if (visitedCount >= indices.NumGridCells)
					break;

				var connections = connectivity[element];
				var bitEnd = connections.BitLoopEndIndex();
				for (var i = 0; i < bitEnd; i++)
				{
					var flag = CellConnectionExtensions.FlagFromBitIndex[i];
					if (!connections.HasBit(flag))
						continue;

					var neighbor = cell + CellConnectionExtensions.OffsetFromBitIndex[i];
					var neighborIndex = indices.CellToIndex(neighbor);
					if (calcGrid[neighborIndex].status == PathFinderJob.CalcNode.Status.Closed)
						continue;

					var cost = IndexCost(grid, providerCost, blocked, neighborIndex);
					if (cost >= CostImpassable)
						continue;

					var diagonal = flag.Diagonal();
					if (diagonal)
					{
						var adjacentA = indices.CellToIndex(new IntVec3(neighbor.x, 0, cell.z));
						var adjacentB = indices.CellToIndex(new IntVec3(cell.x, 0, neighbor.z));
						if (IndexCost(grid, providerCost, blocked, adjacentA) >= CostImpassable ||
							IndexCost(grid, providerCost, blocked, adjacentB) >= CostImpassable ||
							buildings.IsSet(adjacentA) || (!traverseParams.canBashFences && fences.IsSet(adjacentA)) ||
							buildings.IsSet(adjacentB) || (!traverseParams.canBashFences && fences.IsSet(adjacentB)))
						{
							continue;
						}
					}

					cost = diagonal ? cost + moveTicksDiagonal : cost + moveTicksCardinal;
					var newG = cost + calcGrid[element].gCost;

					var status = calcGrid[neighborIndex].status;
					if (status != PathFinderJob.CalcNode.Status.None)
					{
						var slack = 0;
						if (status == PathFinderJob.CalcNode.Status.Closed)
							slack = Mathf.CeilToInt(moveTicksCardinal * 0.8f);
						if (calcGrid[neighborIndex].gCost <= newG + slack)
							continue;
					}

					if (status == PathFinderJob.CalcNode.Status.None)
					{
						touched.Add(neighborIndex);
						var delta = destCell - neighbor;
						if (delta.x != 0 || delta.z != 0)
						{
							var ax = Math.Abs(delta.x);
							var az = Math.Abs(delta.z);
							var heuristic = 1f * (ax + az) + -0.58579004f * Math.Min(ax, az);
							var node = calcGrid[neighborIndex];
							node.hCost = heuristic * 13f * heuristicStrength;
							calcGrid[neighborIndex] = node;
						}
					}

					var f = (float)newG + calcGrid[neighborIndex].hCost;
					if (f < 0f)
						f = 0f;

					var updated = calcGrid[neighborIndex];
					updated.gCost = newG;
					updated.fCost = f;
					updated.parentIndex = element;
					updated.status = PathFinderJob.CalcNode.Status.Open;
					calcGrid[neighborIndex] = updated;
					frontier.Enqueue(neighborIndex, f);
				}

				visitedCount++;
				var closed = calcGrid[element];
				closed.status = PathFinderJob.CalcNode.Status.Closed;
				calcGrid[element] = closed;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int IndexCost(in NativeArray<int>.ReadOnly grid, in NativeArray<ushort>.ReadOnly providerCost,
			in NativeArray<bool>.ReadOnly blocked, int index)
		{
			if (providerCost[index] == ushort.MaxValue)
				return CostImpassable;
			return Math.Max(grid[index], providerCost[index]) + (blocked[index] ? CostBlocked : 0);
		}

		private static void FinalizePath(NativeList<IntVec3> path, NativeArray<PathFinderJob.CalcNode> calcGrid,
			NativeReference<PathFinderJob.ResultData> result, CellIndices indices, int finalIndex)
		{
			var current = finalIndex;
			while (true)
			{
				path.Add(indices.IndexToCell(current));
				var parent = calcGrid[current].parentIndex;
				if (current == parent)
					break;
				current = parent;
			}

			var data = default(PathFinderJob.ResultData);
			data.pathCost = calcGrid[finalIndex].gCost;
			result.Value = data;
		}
	}
}