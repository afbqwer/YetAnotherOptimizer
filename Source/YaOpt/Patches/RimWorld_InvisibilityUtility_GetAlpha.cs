using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers the invisibility alpha from the packed per-pawn state.
	/// </summary>
	/// <remarks>
	/// <c>PawnRenderer.GetDrawParms</c> calls this for every visible pawn every frame, and vanilla
	/// scans the whole hediff list even for pawns that have no invisibility comp at all.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(InvisibilityUtility))]
	[HarmonyPatch(nameof(InvisibilityUtility.GetAlpha))]
	internal static class RimWorld_InvisibilityUtility_GetAlpha
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive && PawnInvisibilityCache.RevealCurveAvailable;
		}

		static bool Prefix(Pawn pawn, ref float __result)
		{
			if (DebugSettings.showHiddenPawns)
			{
				__result = 1f;
				return false;
			}

			if (!PawnInvisibilityCache.TryRead(pawn, out var bits))
				return true;
			if ((bits & PawnInvisibilityCache.HasComp) == 0L)
			{
				__result = 1f;
				return false;
			}

			__result = PawnInvisibilityCache.Alpha(bits);
			return false;
		}
	}
}
