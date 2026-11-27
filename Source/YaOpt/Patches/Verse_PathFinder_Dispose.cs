using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Releases the static scratch bookkeeping keyed by a <see cref="PathFinder"/>'s buffers when the finder is
	/// disposed, so a long session with many map loads does not accumulate dead entries.
	/// </summary>
	/// <remarks>
	/// <see cref="ScratchTouchRegistry"/> keeps per-buffer reset bookkeeping keyed by raw pointer, which cannot
	/// be keyed weakly by buffer. Vanilla's <see cref="PathFinder.Dispose"/> completes all of this finder's jobs
	/// before freeing its buffers, so clearing here is safe.
	/// </remarks>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.Dispose))]
	internal static class Verse_PathFinder_Dispose
	{
		static bool Prepare()
		{
			var settings = YaOptGlobal.Settings;
			return settings.OptPathNodeReset.Enabled || settings.OptPathOverlaySparse.Enabled ||
				settings.OptPathDoorsJobFast.Enabled;
		}

		static void Postfix()
		{
			ScratchTouchRegistry.Clear();
		}
	}
}