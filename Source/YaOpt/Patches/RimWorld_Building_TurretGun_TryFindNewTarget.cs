using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips turret target searches when the map holds no candidate that can be hostile to the turret.
	/// </summary>
	/// <remarks>
	/// Player turrets search the attack target cache on every scan. When the cache holds no
	/// hostile, aggro or factionless candidates for the player faction, that search can only
	/// walk an empty set. Returning Invalid here avoids the call without duplicating work when
	/// candidates exist; manned turrets search from the operator's faction and are left to
	/// vanilla. Other factions are also left alone because their overhead-fire branch can target
	/// buildings that are not registered in the attack target cache.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTurretTargetPrune"/>
	[HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.TryFindNewTarget))]
	internal static class RimWorld_Building_TurretGun_TryFindNewTarget
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptTurretTargetPrune.Enabled;
		}

		static bool Prefix(Building_TurretGun __instance, ref LocalTargetInfo __result)
		{
			if (__instance == null || !__instance.Spawned)
				return true;

			var map = __instance.Map;
			if (map == null || map.attackTargetsCache == null)
				return true;

			// Manned turrets search from the operator's faction, not the turret's.
			if (__instance.mannableComp != null && __instance.mannableComp.MannedNow)
				return true;

			var faction = __instance.Faction;
			if (faction != Faction.OfPlayer)
				return true;

			var cache = map.attackTargetsCache;
			if (cache.TargetsHostileToFaction(faction).Count == 0 &&
				cache.pawnsInAggroMentalState.Count == 0 &&
				cache.factionlessHumanlikes.Count == 0)
			{
				__result = LocalTargetInfo.Invalid;
				return false;
			}

			return true;
		}
	}
}
