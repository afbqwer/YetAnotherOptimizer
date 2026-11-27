using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	[HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect))]
	internal static class Verse_HediffSet_AddDirect_ShamblerState
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static void Postfix(HediffSet __instance, Hediff hediff)
		{
			if (hediff.def == HediffDefOf.ShamblerCorpse)
				PawnShamblerCache.Rebuild(__instance.pawn);
		}
	}

	[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
	internal static class Verse_Pawn_HealthTracker_RemoveHediff_ShamblerState
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static void Postfix(Hediff hediff)
		{
			if (hediff.def == HediffDefOf.ShamblerCorpse)
				PawnShamblerCache.Rebuild(hediff.pawn);
		}
	}

	[HarmonyPatch(typeof(HediffSet), nameof(HediffSet.Clear))]
	internal static class Verse_HediffSet_Clear_ShamblerState
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static void Postfix(HediffSet __instance)
		{
			PawnShamblerCache.Rebuild(__instance.pawn);
		}
	}

	[HarmonyPatch(typeof(HediffSet), nameof(HediffSet.ExposeData))]
	internal static class Verse_HediffSet_ExposeData_ShamblerState
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static void Postfix(HediffSet __instance)
		{
			if (Scribe.mode != LoadSaveMode.Saving)
				PawnShamblerCache.Rebuild(__instance.pawn);
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
	internal static class Verse_Pawn_ShamblerSpawnSetup
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static void Postfix(Thing __instance)
		{
			if (__instance is Pawn pawn)
				PawnShamblerCache.Rebuild(pawn);
		}
	}
}
