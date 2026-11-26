using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reuses <see cref="JobGiver_TakeDrugsForDrugPolicy.GetPriority"/> for the current tick.
	/// </summary>
	/// <remarks>
	/// The value depends only on the pawn's drug policy, needs and tick, and
	/// <c>ThinkNode_PrioritySorter</c> asks for it again after every failed child.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugPolicyCache"/>
	/// <seealso cref="DrugJobGiverPriorityCache"/>
	[HarmonyPatch(typeof(JobGiver_TakeDrugsForDrugPolicy),
		nameof(JobGiver_TakeDrugsForDrugPolicy.GetPriority))]
	internal static class RimWorld_JobGiver_TakeDrugsForDrugPolicy_GetPriority
	{
		static bool Prepare()
		{
			DrugJobGiverPriorityCache.Clear();
			return YaOptGlobal.Settings?.OptDrugPolicyCache.Enabled == true;
		}

		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(Pawn pawn, ref float __result)
		{
			return !DrugJobGiverPriorityCache.TryGetTakeDrugs(pawn, out __result);
		}

		// Run first among postfixes so the cache stores the original getter's value; a foreign postfix
		// that transforms the result then replays on top of the cached value exactly as it did on the
		// call that filled the cache.
		[HarmonyPriority(Priority.First)]
		static void Postfix(Pawn pawn, float __result, bool __runOriginal)
		{
			if (__runOriginal)
				DrugJobGiverPriorityCache.StoreTakeDrugs(pawn, __result);
		}
	}
}