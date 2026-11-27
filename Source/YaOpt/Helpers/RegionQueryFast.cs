using System.Runtime.CompilerServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reads the region grid without the per-call rebuild bookkeeping when there is nothing to rebuild.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>RegionAndRoomQuery.RegionAt</c> and <c>RegionGrid.GetValidRegionAt</c> both first check whether
	/// the region grid needs a rebuild. When <see cref="RegionAndRoomUpdater.AnythingToRebuild"/> is
	/// false, <c>TryRebuildDirtyRegionsAndRooms</c> only writes <c>working</c> twice and returns, so
	/// reading the grid at that point is equivalent to vanilla.
	/// </para>
	/// <para>
	/// The bounds check is intentionally left to the caller: <c>RegionAt</c> returns null silently for
	/// out-of-bounds cells while <c>GetValidRegionAt</c> logs an error, so the caller must fall back to
	/// vanilla for those cells.
	/// </para>
	/// </remarks>
	internal static class RegionQueryFast
	{
		/// <summary>
		/// Reads the region at an in-bounds cell when nothing is pending rebuild.
		/// </summary>
		/// <returns>
		/// False when the caller must run the vanilla method (regions dirty or not initialized yet).
		/// When true, <paramref name="region"/> is the same value the vanilla lookup would return,
		/// including null for disposed grids and stale entries.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryGetCleanRegion(Map map, IntVec3 c, out Region region)
		{
			RegionGrid grid = map.regionGrid;
			if (grid.disposed)
			{
				region = null;
				return true;
			}

			if (map.regionAndRoomUpdater.AnythingToRebuild)
			{
				region = null;
				return false;
			}

			region = grid.regionGrid[map.cellIndices.CellToIndex(c)];
			if (region != null && !region.valid)
				region = null;

			return true;
		}
	}
}