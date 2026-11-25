using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Republishes the packed invisibility state from the per-tick update.
	/// </summary>
	/// <remarks>
	/// <c>CompPostTick</c> assigns <c>wasForcedVisibleLastTick = ForcedVisible</c> unconditionally,
	/// so reading that field here reuses the value vanilla already computed instead of evaluating
	/// the eight-branch predicate a second time.
	///
	/// <c>__runOriginal</c> guards the case where another mod's prefix skipped the
	/// vanilla body: the field would still hold the previous tick's value, which must not be
	/// published as this tick's answer.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(HediffComp_Invisibility))]
	[HarmonyPatch(nameof(HediffComp_Invisibility.CompPostTick))]
	internal static class Verse_HediffComp_Invisibility_CompPostTick
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static void Postfix(HediffComp_Invisibility __instance, bool __runOriginal)
		{
			if (!__runOriginal)
				return;
			PawnInvisibilityCache.Publish(__instance);
		}
	}
}
