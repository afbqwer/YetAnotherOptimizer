using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <seealso cref="YaOptSettings.OptRefuelSearchCache"/>
	[HarmonyPatch(typeof(CompRefuelable))]
	[HarmonyPatch(nameof(CompRefuelable.PostSpawnSetup))]
	internal static class RimWorld_CompRefuelable_PostSpawnSetup
	{
		static bool Prepare()
		{
			return PrepatcherState.RefuelCandidatesActive;
		}

		static void Postfix(CompRefuelable __instance)
		{
			RefuelWorkCandidates.NotifyChanged(__instance);
		}
	}

	/// <seealso cref="YaOptSettings.OptRefuelSearchCache"/>
	[HarmonyPatch(typeof(ThingComp))]
	[HarmonyPatch(nameof(ThingComp.PostDeSpawn))]
	internal static class Verse_ThingComp_PostDeSpawn_Refuel
	{
		static bool Prepare()
		{
			return PrepatcherState.RefuelCandidatesActive;
		}

		static void Prefix(ThingComp __instance, Map map)
		{
			if (__instance is CompRefuelable refuelable)
				RefuelWorkCandidates.NotifyRemoved(refuelable, map);
		}
	}

	/// <seealso cref="YaOptSettings.OptRefuelSearchCache"/>
	[HarmonyPatch(typeof(CompRefuelable))]
	[HarmonyPatch(nameof(CompRefuelable.PostDestroy))]
	internal static class RimWorld_CompRefuelable_PostDestroy
	{
		static bool Prepare()
		{
			return PrepatcherState.RefuelCandidatesActive;
		}

		static void Prefix(CompRefuelable __instance, Map previousMap)
		{
			RefuelWorkCandidates.NotifyRemoved(__instance, previousMap);
		}
	}
}