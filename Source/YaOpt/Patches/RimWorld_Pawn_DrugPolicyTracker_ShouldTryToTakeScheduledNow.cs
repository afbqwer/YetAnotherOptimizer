using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reuses <see cref="Pawn_DrugPolicyTracker.ShouldTryToTakeScheduledNow"/> for the current tick.
	/// </summary>
	/// <remarks>
	/// The scheduled-drug job giver asks this once while computing its priority and again while
	/// building the job, and the priority sorter can ask for the priority repeatedly. The result is
	/// dropped when a drug is ingested or a suspension ends, and it is never cached while paused so
	/// a policy edit cannot be outlived.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugScheduleCache"/>
	/// <seealso cref="DrugScheduledEvalCache"/>
	[HarmonyPatch(typeof(Pawn_DrugPolicyTracker),
		nameof(Pawn_DrugPolicyTracker.ShouldTryToTakeScheduledNow))]
	internal static class RimWorld_Pawn_DrugPolicyTracker_ShouldTryToTakeScheduledNow
	{
		static bool Prepare()
		{
			DrugScheduledEvalCache.Clear();
			return YaOptGlobal.Settings?.OptDrugScheduleCache.Enabled == true;
		}

		// Run after foreign prefixes so an external short-circuit still wins.
		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(Pawn_DrugPolicyTracker __instance, ThingDef ingestible, ref bool __result)
		{
			return !DrugScheduledEvalCache.TryGet(__instance.pawn, __instance.CurrentPolicy, ingestible,
				out __result);
		}

		// Run first among postfixes so the cache stores the original value; a foreign postfix that
		// transforms the result then replays on top of the cached value.
		[HarmonyPriority(Priority.First)]
		static void Postfix(Pawn_DrugPolicyTracker __instance, ThingDef ingestible, bool __result,
			bool __runOriginal)
		{
			if (__runOriginal)
				DrugScheduledEvalCache.Store(__instance.pawn, __instance.CurrentPolicy, ingestible, __result);
		}
	}
}