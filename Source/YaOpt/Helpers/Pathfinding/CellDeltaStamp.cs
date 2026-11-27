using System;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Per-cell "seen this gather" stamps used to deduplicate expanded cell deltas without hashing an
	/// <see cref="IntVec3"/> per cell.
	/// </summary>
	/// <remarks>
	/// Each gather bumps a generation; a cell is new when its stamp differs, and recording it is a single
	/// store. The stamp array is never cleared per gather (only on growth or after 2^31 gathers), so a
	/// gather costs one store per visited cell instead of a hash-set insert.
	/// </remarks>
	internal sealed class CellDeltaStamp
	{
		private int[] _stamp = Array.Empty<int>();
		private int _generation;

		internal void Begin(int cells)
		{
			if (_stamp.Length < cells)
			{
				_stamp = new int[cells];
				_generation = 0;
			}
			if (++_generation == int.MaxValue)
			{
				Array.Clear(_stamp, 0, _stamp.Length);
				_generation = 1;
			}
		}

		internal bool Mark(int index)
		{
			if (_stamp[index] == _generation)
				return false;
			_stamp[index] = _generation;
			return true;
		}
	}
}