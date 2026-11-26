using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Invalidates <see cref="DrugScheduledEvalCache"/> when a pawn's drug take records change.
	/// </summary>
	/// <remarks>
	/// <c>Notify_DrugIngested</c> updates <c>lastTakenTicks</c> and the daily counter;
	/// <c>Notify_LeftSuspension</c> shifts <c>lastTakenTicks</c>. Both change the outcome of
	/// <c>ShouldTryToTakeScheduledNow</c> within the same tick.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugScheduleCache"/>
	[HarmonyPatch]
	internal static class MultiTargets_DrugTakeRecordNotifications
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Pawn_DrugPolicyTracker),
				nameof(Pawn_DrugPolicyTracker.Notify_DrugIngested));
			yield return AccessTools.Method(typeof(Pawn_DrugPolicyTracker),
				nameof(Pawn_DrugPolicyTracker.Notify_LeftSuspension));
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptDrugScheduleCache.Enabled == true;
		}

		static void Postfix(Pawn_DrugPolicyTracker __instance)
		{
			DrugScheduledEvalCache.Invalidate(__instance.pawn);
		}
	}
}