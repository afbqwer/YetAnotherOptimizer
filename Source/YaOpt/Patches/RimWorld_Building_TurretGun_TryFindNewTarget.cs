using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace YaOpt.Patches
{
	/// <summary>
	/// Short-circuits turret target searches when there is obviously nothing to shoot.
	/// </summary>
	/// <remarks>
	/// <see cref="Building_TurretGun.TryFindNewTarget"/> runs every 15 ticks and allocates delegates,
	/// copies the candidate list and scans it before checking range. When the candidate lists are empty,
	/// or every candidate is out of range, that work is wasted.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTurretTargetPrune"/>
	[HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.TryFindNewTarget))]
	internal static class RimWorld_Building_TurretGun_TryFindNewTarget
	{
		private const int MaxPruneCandidates = 64;

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

			// Manned turrets search from the operator's verb, which is not necessarily the turret's.
			var mannable = __instance.GetComp<CompMannable>();
			if (mannable != null && mannable.MannedNow)
				return true;

			var faction = __instance.Faction;
			if (faction == null)
				return true;

			var verb = __instance.AttackVerb;
			if (verb == null)
				return true;

			var cache = map.attackTargetsCache;

			var hostileCount = cache.TargetsHostileToFaction(faction).Count;
			if (hostileCount == 0 &&
				cache.pawnsInAggroMentalState.Count == 0 &&
				cache.factionlessHumanlikes.Count == 0)
			{
				__result = LocalTargetInfo.Invalid;
				return false;
			}

			if (hostileCount > MaxPruneCandidates)
				return true;

			List<IAttackTarget> candidates = cache.GetPotentialTargetsFor(__instance);
			if (candidates.Count == 0)
			{
				__result = LocalTargetInfo.Invalid;
				return false;
			}

			var maxDist = verb.EffectiveRange;
			var origin = __instance.Position;
			for (var i = 0; i < candidates.Count; i++)
			{
				var thing = candidates[i]?.Thing;
				if (thing != null && thing.Position.InHorDistOf(origin, maxDist))
					return true;
			}

			__result = LocalTargetInfo.Invalid;
			return false;
		}
	}
}