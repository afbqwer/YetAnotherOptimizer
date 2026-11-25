using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Rebuilds the packed invisibility state when an invisibility comp is added.
	/// </summary>
	/// <remarks>
	/// This is the only place that recomputes the structural bits (presence, comp count, subclass).
	/// Getting them wrong would leave a pawn permanently invisible or permanently visible, so they
	/// are driven by events rather than by the per-tick refresh.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(HediffComp_Invisibility))]
	[HarmonyPatch(nameof(HediffComp_Invisibility.CompPostPostAdd))]
	internal static class Verse_HediffComp_Invisibility_CompPostPostAdd
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static void Postfix(HediffComp_Invisibility __instance)
		{
			var pawn = __instance.parent?.pawn;
			if (pawn != null)
				PawnInvisibilityCache.Rebuild(pawn);
		}
	}
}
