using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers the hidden-from-player query from the packed per-pawn state.
	/// </summary>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(InvisibilityUtility))]
	[HarmonyPatch(nameof(InvisibilityUtility.IsHiddenFromPlayer))]
	internal static class RimWorld_InvisibilityUtility_IsHiddenFromPlayer
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static bool Prefix(Pawn pawn, ref bool __result)
		{
			// Both early exits are cheaper than the cache read, and they match vanilla order.
			if (DebugSettings.showHiddenPawns || pawn.Faction == Faction.OfPlayer)
			{
				__result = false;
				return false;
			}

			if (!PawnInvisibilityCache.TryRead(pawn, out var bits))
				return true;
			__result = (bits & PawnInvisibilityCache.HasComp) != 0L &&
				(bits & PawnInvisibilityCache.VisibleToPlayer) == 0L &&
				!PawnInvisibilityCache.IsPsychologicallyVisible(bits);
			return false;
		}
	}
}
