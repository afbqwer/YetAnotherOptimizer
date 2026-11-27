using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reads the region directly when the grid has nothing to rebuild instead of running the
	/// disabled-updater warning, the rebuild check and the duplicate bounds check on every call.
	/// </summary>
	/// <remarks>
	/// Direct callers such as reachability queries bypass <c>RegionAt</c> and call this method. The
	/// fast path keeps the out-of-bounds error log and every rebuild/disabled-updater path on the
	/// vanilla branch.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRegionQueryFast"/>
	/// <seealso cref="RegionQueryFast"/>
	[HarmonyPatch(typeof(RegionGrid), nameof(RegionGrid.GetValidRegionAt))]
	internal static class Verse_RegionGrid_GetValidRegionAt
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptRegionQueryFast.Enabled == true;
		}

		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(RegionGrid __instance, IntVec3 c, ref Region __result)
		{
			if (!c.InBounds(__instance.map))
				return true;

			if (!RegionQueryFast.TryGetCleanRegion(__instance.map, c, out Region region))
				return true;

			__result = region;
			return false;
		}
	}
}