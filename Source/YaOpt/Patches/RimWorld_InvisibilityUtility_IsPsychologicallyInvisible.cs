using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers the psychological-invisibility query from the packed per-pawn state.
	/// </summary>
	/// <remarks>
	/// Vanilla walks every hediff and calls <c>TryGetComp</c> on each. Patching the utility instead
	/// of its call sites covers rendering, AI, UI and third-party callers at once.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(InvisibilityUtility))]
	[HarmonyPatch(nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
	internal static class RimWorld_InvisibilityUtility_IsPsychologicallyInvisible
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static bool Prefix(Pawn pawn, ref bool __result)
		{
			if (!PawnInvisibilityCache.TryRead(pawn, out var bits))
				return true;
			__result = (bits & PawnInvisibilityCache.HasComp) != 0L &&
				!PawnInvisibilityCache.IsPsychologicallyVisible(bits);
			return false;
		}
	}
}
