using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Avoids allocating two history events when a pawn has no ideo.
	/// </summary>
	/// <remarks>
	/// DoerWillingToDo is always true without an ideo, so only the DrugDesire trait remains relevant.
	/// Pawns with an ideo keep vanilla behaviour, including its random checks.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTeetotalerFastPath"/>
	[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.IsTeetotaler), typeof(Pawn))]
	internal static class RimWorld_PawnUtility_IsTeetotaler
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptTeetotalerFastPath.Enabled == true;
		}

		static bool Prefix(Pawn pawn, ref bool __result)
		{
			if (pawn == null || pawn.Ideo != null)
				return true;

			if (pawn.story == null)
			{
				__result = false;
				return false;
			}

			__result = pawn.story.traits.DegreeOfTrait(TraitDefOf.DrugDesire) < 0;
			return false;
		}
	}
}
