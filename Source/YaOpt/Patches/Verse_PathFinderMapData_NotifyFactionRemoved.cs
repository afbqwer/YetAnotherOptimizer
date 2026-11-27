using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Forces a full grid recycle when a faction's cost array is disposed, because a cached template may
	/// still hold a read-only view of it.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptPathIncrementalGrid"/>
	[HarmonyPatch(typeof(PathFinderMapData))]
	[HarmonyPatch(nameof(PathFinderMapData.Notify_FactionRemoved))]
	internal static class Verse_PathFinderMapData_NotifyFactionRemoved
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathIncrementalGrid.Enabled;
		}

		static void Postfix(PathFinderMapData __instance)
		{
			PathGridIncremental.NotifyFactionRemoved(__instance.map.pathFinder);
		}
	}
}