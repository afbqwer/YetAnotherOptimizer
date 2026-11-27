using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Releases the static pathfinding bookkeeping keyed by a live <see cref="PathFinder"/> when the finder is
	/// disposed, so a long session with many map loads does not accumulate dead entries.
	/// </summary>
	/// <remarks>
	/// <see cref="PathGridIncremental"/> keeps a strong <see cref="PathFinder"/> set for forced recycles, and
	/// <see cref="ScratchTouchRegistry"/> keeps per-buffer reset bookkeeping keyed by raw pointer. Neither can
	/// be keyed weakly by buffer, and vanilla's <see cref="PathFinder.Dispose"/> completes all of this finder's
	/// jobs before freeing its buffers, so clearing here is safe.
	/// </remarks>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.Dispose))]
	internal static class Verse_PathFinder_Dispose
	{
		static bool Prepare()
		{
			var settings = YaOptGlobal.Settings;
			return settings.OptPathIncrementalGrid.Enabled || settings.OptPathNodeReset.Enabled ||
				settings.OptPathOverlaySparse.Enabled || settings.OptPathDoorsJobFast.Enabled;
		}

		static void Postfix(PathFinder __instance)
		{
			PathGridIncremental.Clear(__instance);
			ScratchTouchRegistry.Clear();
		}
	}
}