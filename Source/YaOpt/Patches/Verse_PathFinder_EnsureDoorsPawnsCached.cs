using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Rebuilds the per-tick door/pawn snapshot but keeps only pawns that can actually block, shrinking the
	/// per-request pawn scan in <see cref="PathGridDoorsBlockedJob.Execute"/>.
	/// </summary>
	/// <remarks>
	/// Vanilla copies every spawned pawn and filters each one inside every path's job. The collision test is a
	/// pure state read that is stable within a tick, so it can run once here instead.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathDoorsJobFast"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.EnsureDoorsPawnsCached))]
	internal static class Verse_PathFinder_EnsureDoorsPawnsCached
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathDoorsJobFast.Enabled;
		}

		static bool Prefix(PathFinder __instance)
		{
			var map = __instance.map;
			if (map == null)
				return true;
			if (__instance.cachedTick == GenTicks.TicksGame)
				return false;

			__instance.cachedTick = GenTicks.TicksGame;
			__instance.cachedProviders.Clear();
			__instance.cachedDoors.Clear();
			__instance.cachedPawns.Clear();

			foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.CostProvider))
				__instance.cachedProviders.Add((IPathFindCostProvider)thing);

			__instance.cachedDoors.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Door));
			for (var i = 0; i < __instance.cachedDoors.Count; i++)
			{
				if (__instance.cachedDoors[i] is Building_Door door)
					door.SnapshotWillCloseSoonForJobs();
			}

			var spawned = map.mapPawns.AllPawnsSpawned;
			for (var i = 0; i < spawned.Count; i++)
			{
				if (PawnUtility.ShouldCollideWithPawns(spawned[i]))
					__instance.cachedPawns.Add(spawned[i]);
			}
			return false;
		}
	}
}