using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers <see cref="ApparelUtility.HasPartsToWear"/> from the pawn's body part groups collected
	/// once per apparel generation call.
	/// </summary>
	/// <remarks>
	/// The set is worn through two body part checks per item - one in the apparel set, one in the
	/// apparel tracker - and every check rebuilds the not-missing part enumeration for each body part
	/// group. Body parts cannot change during generation, so one union answers them all. Disabled when
	/// another mod patches the method, because a replay would skip its code.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches.TryGetWearableGroups"/>
	[HarmonyPatch(typeof(ApparelUtility), nameof(ApparelUtility.HasPartsToWear))]
	internal static class RimWorld_ApparelUtility_HasPartsToWear
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled != true || PawnGearRebuildCompat.Ready)
				return false;

			return !PatchConflictGuard.HasForeignPatch(
				AccessTools.Method(typeof(ApparelUtility), nameof(ApparelUtility.HasPartsToWear)));
		}

		static bool Prefix(Pawn p, ThingDef apparel, ref bool __result)
		{
			if (!PawnGearSessionCaches.TryGetWearableGroups(p, out var groups))
				return true;

			var defGroups = apparel.apparel.bodyPartGroups;
			for (var i = 0; i < defGroups.Count; i++)
			{
				if (groups.Contains(defGroups[i]))
				{
					__result = true;
					return false;
				}
			}

			__result = false;
			return false;
		}
	}
}