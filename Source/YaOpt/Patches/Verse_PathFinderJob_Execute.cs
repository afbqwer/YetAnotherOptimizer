using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PathFinderJob.Execute"/> with the sparse-reset version that avoids clearing the
	/// whole <c>calcGrid</c> (20 bytes per cell) for every path.
	/// </summary>
	/// <remarks>
	/// The prefix skips the original entirely. A guard falls back to vanilla if the injected instance looks
	/// uninitialized, so a Harmony that cannot supply a valid value-type <c>__instance</c> can never run the
	/// A* on empty inputs.
	/// </remarks>
	/// <seealso cref="OptimizedPathFinderJob"/>
	/// <seealso cref="YaOptSettings.OptPathNodeReset"/>
	[HarmonyPatch(typeof(PathFinderJob))]
	[HarmonyPatch(nameof(PathFinderJob.Execute))]
	internal static class Verse_PathFinderJob_Execute
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathNodeReset.Enabled;
		}

		static bool Prefix(PathFinderJob __instance)
		{
			if (!__instance.calcGrid.IsCreated || !__instance.frontier.IsCreated || __instance.indices.NumGridCells <= 0)
				return true;

			OptimizedPathFinderJob.Execute(__instance);
			return false;
		}
	}
}