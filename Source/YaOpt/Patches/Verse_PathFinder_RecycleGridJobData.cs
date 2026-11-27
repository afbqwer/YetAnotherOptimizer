using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Drops stored grid templates whenever the grids themselves are recycled, so a template never outlives
	/// its output array.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptPathIncrementalGrid"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.RecycleGridJobData))]
	internal static class Verse_PathFinder_RecycleGridJobData
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathIncrementalGrid.Enabled;
		}

		static void Postfix(PathFinder __instance)
		{
			PathGridIncremental.Clear(__instance);
		}
	}
}