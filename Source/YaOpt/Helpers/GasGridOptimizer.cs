using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces <see cref="GasGrid.Tick"/>'s two fixed-size random-cell sweeps with a bitmap-driven
	/// one: only 64-cell buckets that actually contain gas are visited.
	/// </summary>
	/// <remarks>
	/// The vanilla loop visits <c>area/64</c> dissipation positions and <c>area/32</c> diffusion
	/// positions every tick regardless of where the gas is, which costs ~20 us per tick on a 250x250
	/// map. The bitmap keeps the same positions per tick, so every cell is still processed once per
	/// 64/32 ticks, but empty buckets cost a single word test.
	/// <para>
	/// The bitmap is in <c>cellsInRandomOrder</c> order, so the iteration order and therefore the
	/// gas simulation result stays identical to vanilla. It is maintained from <c>SetDirect</c> and
	/// fully rebuilt every 600 ticks, so a write path we did not hook self-heals quickly.
	/// </para>
	/// </remarks>
	internal static class GasGridOptimizer
	{
		private const int AnyGasCheckInterval = 600;

		private static readonly ConditionalWeakTable<GasGrid, GasState> States =
			new ConditionalWeakTable<GasGrid, GasState>();

		[ThreadStatic] private static GasGrid _cachedGrid;
		[ThreadStatic] private static GasState _cachedState;

		/// <summary>
		/// Drops all cached state. Called when the patches are (re)applied so the occupancy bitmap is
		/// rebuilt from the density array instead of trusting a bitmap that stopped updating while the
		/// option was off.
		/// </summary>
		internal static void Reset()
		{
			States.Clear();
			_cachedGrid = null;
			_cachedState = null;
		}

		internal sealed class GasState
		{
			public ulong[] Coverage = Array.Empty<ulong>();

			/// <summary>Random-order position -> natural cell index.</summary>
			public int[] Indices = Array.Empty<int>();

			/// <summary>Natural cell index -> random-order position.</summary>
			public int[] Reverse = Array.Empty<int>();

			/// <summary>Random-order position -> cell, used by diffusion which needs the cell position.</summary>
			public List<IntVec3> Cells;

			public int Area;
			public bool Ready;
		}

		/// <summary>Called from the <c>SetDirect</c> postfixes; cheap no-op until the state exists.</summary>
		internal static void NotifyWrite(GasGrid grid, int index)
		{
			var state = GetReadyState(grid);
			if (state == null)
			{
				return;
			}

			var pos = state.Reverse[index];
			ref var word = ref state.Coverage[pos >> 6];
			var bit = 1UL << (pos & 63);
			if (grid.gasDensity[index] != 0)
			{
				word |= bit;
			}
			else
			{
				word &= ~bit;
			}
		}

		/// <summary>Returns true when this method replaced the vanilla tick.</summary>
		internal static bool Tick(GasGrid grid)
		{
			if (!grid.CalculateGasEffects)
			{
				return false;
			}

			var state = Ensure(grid);
			if (state == null)
			{
				return false;
			}

			TickCells(grid, state, out var dissipationCursor, dissipation: true);
			grid.cycleIndexDissipation = dissipationCursor;

			TickCells(grid, state, out var diffusionCursor, dissipation: false);
			grid.cycleIndexDiffusion = diffusionCursor;

			if (grid.map.IsHashIntervalTick(AnyGasCheckInterval))
			{
				grid.RecalculateEverHadGas();
				RebuildCoverage(grid, state);
			}

			return true;
		}

		private static void TickCells(GasGrid grid, GasState state, out int finalCursor,
			bool dissipation)
		{
			var area = state.Area;
			var count = dissipation ? (area + 63) >> 6 : (area + 31) >> 5;
			var cursor = dissipation ? grid.cycleIndexDissipation : grid.cycleIndexDiffusion;
			var coverage = state.Coverage;
			var indices = state.Indices;
			var cells = state.Cells;

			var pos = cursor;
			var remaining = count;
			while (remaining > 0)
			{
				if (pos >= area)
				{
					pos = 0;
				}

				var bucket = pos >> 6;
				var offset = pos & 63;
				var bits = coverage[bucket] & (ulong.MaxValue << offset);
				if (bits == 0)
				{
					var step = 64 - offset;
					var toEnd = area - pos;
					if (step > toEnd)
					{
						step = toEnd;
					}
					if (step > remaining)
					{
						step = remaining;
					}
					pos += step;
					remaining -= step;
					continue;
				}

				var bit = BitOperations.TrailingZeroCount(bits);
				var visit = (bucket << 6) + bit;
				if (dissipation)
				{
					grid.TryDissipateGases(indices[visit]);
				}
				else
				{
					grid.TryDiffuseGases(cells[visit]);
				}

				remaining -= bit - offset + 1;
				pos = visit + 1;
			}

			finalCursor = (cursor + count) % area;
		}

		private static GasState Ensure(GasGrid grid)
		{
			if (_cachedGrid == grid && _cachedState != null && _cachedState.Ready)
			{
				return _cachedState;
			}

			// GetValue is atomic; Add would race if a profiling mod ticks different maps on
			// different threads.
			var state = States.GetValue(grid, static _ => new GasState());
			if (!state.Ready && !Initialize(grid, state))
			{
				return null;
			}

			_cachedGrid = grid;
			_cachedState = state;
			return state;
		}

		private static GasState GetReadyState(GasGrid grid)
		{
			if (_cachedGrid == grid)
			{
				return _cachedState != null && _cachedState.Ready ? _cachedState : null;
			}

			if (States.TryGetValue(grid, out var state) && state.Ready)
			{
				_cachedGrid = grid;
				_cachedState = state;
				return state;
			}

			return null;
		}

		private static bool Initialize(GasGrid grid, GasState state)
		{
			var cells = grid.map.cellsInRandomOrder.GetAll();
			var area = grid.map.Area;
			var numCells = grid.map.cellIndices.NumGridCells;
			if (cells == null || cells.Count != area || numCells != area)
			{
				return false;
			}

			var sizeX = grid.map.Size.x;
			var indices = new int[area];
			var reverse = new int[numCells];
			for (var i = 0; i < area; i++)
			{
				var index = CellIndicesUtility.CellToIndex(cells[i], sizeX);
				indices[i] = index;
				reverse[index] = i;
			}

			state.Cells = cells;
			state.Indices = indices;
			state.Reverse = reverse;
			state.Coverage = new ulong[(area + 63) >> 6];
			state.Area = area;

			RebuildCoverage(grid, state);
			state.Ready = true;
			return true;
		}

		private static void RebuildCoverage(GasGrid grid, GasState state)
		{
			var gas = grid.gasDensity;
			var coverage = state.Coverage;
			var reverse = state.Reverse;
			Array.Clear(coverage, 0, coverage.Length);

			var i = 0;
			var length = gas.Length;
			if (Vector256.IsHardwareAccelerated)
			{
				var zero = Vector256<uint>.Zero;
				for (; i + Vector256<uint>.Count <= length; i += Vector256<uint>.Count)
				{
					if (Vector256.EqualsAll(Vector256.LoadUnsafe(ref gas[i]), zero))
					{
						continue;
					}

					for (var j = 0; j < Vector256<uint>.Count; j++)
					{
						SetBit(coverage, reverse, i + j, gas[i + j]);
					}
				}
			}

			for (; i < length; i++)
			{
				SetBit(coverage, reverse, i, gas[i]);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void SetBit(ulong[] coverage, int[] reverse, int index, uint value)
		{
			if (value == 0)
			{
				return;
			}

			var pos = reverse[index];
			coverage[pos >> 6] |= 1UL << (pos & 63);
		}
	}
}
