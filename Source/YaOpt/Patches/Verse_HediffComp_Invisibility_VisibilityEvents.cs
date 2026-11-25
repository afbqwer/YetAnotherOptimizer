using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Republishes immediately when the invisibility state is switched, instead of waiting for the
	/// next tick.
	/// </summary>
	/// <remarks>
	/// The per-tick publish already covers the steady state, but a reveal or a disruption changes
	/// the answer in the middle of a tick: without these hooks the cached value would lag by one
	/// tick, so a pawn revealed by damage or by a script would stay invisible for up to 16 ms.
	/// All three entry points are low frequency, so the extra publish is free.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(HediffComp_Invisibility))]
	internal static class Verse_HediffComp_Invisibility_VisibilityEvents
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		[HarmonyPatch(nameof(HediffComp_Invisibility.BecomeVisible))]
		[HarmonyPostfix]
		static void BecomeVisiblePostfix(HediffComp_Invisibility __instance)
		{
			Publish(__instance);
		}

		[HarmonyPatch(nameof(HediffComp_Invisibility.BecomeInvisible))]
		[HarmonyPostfix]
		static void BecomeInvisiblePostfix(HediffComp_Invisibility __instance)
		{
			Publish(__instance);
		}

		[HarmonyPatch(nameof(HediffComp_Invisibility.DisruptInvisibility))]
		[HarmonyPostfix]
		static void DisruptInvisibilityPostfix(HediffComp_Invisibility __instance)
		{
			Publish(__instance);
		}

		private static void Publish(HediffComp_Invisibility comp)
		{
			var pawn = comp.parent?.pawn;
			if (pawn != null)
				PawnInvisibilityCache.PublishEvaluated(comp);
		}
	}
}
