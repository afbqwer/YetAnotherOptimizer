using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reads the region directly when the grid has nothing to rebuild instead of going through
	/// <see cref="RegionGrid.GetValidRegionAt"/> and its per-call rebuild bookkeeping.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>RegionAt</c> is the common entry for <c>IntVec3.GetRoom</c> and <c>Thing.GetRoom</c>: it
	/// checks bounds, asks <c>GetValidRegionAt</c> for a region and applies the region-type mask.
	/// When nothing is pending rebuild, the middle step only re-checks bounds, state and the dirty
	/// set before returning the same array entry.
	/// </para>
	/// <para>
	/// Out-of-bounds cells, a disposed grid, fixed default arguments and any pending rebuild fall back
	/// to vanilla so its logging and rebuild path stay untouched.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRegionQueryFast"/>
	/// <seealso cref="RegionQueryFast"/>
	[HarmonyPatch(typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.RegionAt))]
	internal static class Verse_RegionAndRoomQuery_RegionAt
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptRegionQueryFast.Enabled == true;
		}

		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(IntVec3 c, Map map, RegionType allowedRegionTypes, ref Region __result)
		{
			if (!c.InBounds(map))
			{
				// RegionAt returns null for out-of-bounds cells without logging.
				__result = null;
				return false;
			}

			if (!RegionQueryFast.TryGetCleanRegion(map, c, out Region region))
				return true;

			__result = region != null && (region.type & allowedRegionTypes) != 0 ? region : null;
			return false;
		}
	}
}