using HarmonyLib;
using RimWorld;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YaOpt.Patches
{
	/// <summary>Limits every alert, including Critical alerts, to one recalculation every 60 frames.</summary>
	[HarmonyPatch(typeof(Alert), nameof(Alert.Recalculate))]
	internal static class RimWorld_Alert_Recalculate
	{
		private const int MinimumIntervalFrames = 60;

		private static readonly ConditionalWeakTable<Alert, RecalculationState> States =
			new ConditionalWeakTable<Alert, RecalculationState>();

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptAlertGlobalThrottle.Enabled == true;
		}

		static bool Prefix(Alert __instance, ref RecalculationState __state)
		{
			__state = null;
			if (!YaOptGlobal.IsInMainThread)
				return true;

			var state = States.GetOrCreateValue(__instance);
			if (Time.frameCount - state.LastFrame < MinimumIntervalFrames)
				return false;

			__state = state;
			return true;
		}

		static void Postfix(RecalculationState __state)
		{
			if (__state != null)
				__state.LastFrame = Time.frameCount;
		}

		private sealed class RecalculationState
		{
			public int LastFrame = -MinimumIntervalFrames;
		}
	}
}
