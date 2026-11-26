using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the alert scan loop's recalculation call with a cost-adaptive scheduler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla <see cref="AlertsReadout.AlertsReadoutUpdate"/> calls
	/// <c>CheckAddOrRemoveAlert(AllAlerts[i])</c> for one stride of alerts per frame, so an alert whose
	/// <c>GetReport()</c> walks the whole map pays that cost 2.5 times per second. Only that first call
	/// site is redirected: the quest, precept, scenario and action-delay blocks below it keep their
	/// vanilla 20-frame timers, and <see cref="AlertThrottleHelper"/> leaves critical alerts and the
	/// call's <c>forceRemove</c> behavior untouched.
	/// </para>
	/// <para>
	/// The IL match is deliberately narrow: the call must still be fed by the <c>AllAlerts</c> field
	/// with a compiled <c>false</c> argument. If another mod or a future vanilla build changes that
	/// shape, the transpiler logs one warning and returns the original instructions instead of
	/// emitting IL it cannot verify.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptAlertThrottle"/>
	/// <seealso cref="AlertThrottleHelper"/>
	[HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutUpdate))]
	internal static class RimWorld_AlertsReadout_AlertsReadoutUpdate
	{
		private static readonly MethodInfo _checkAddOrRemoveAlert =
			AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.CheckAddOrRemoveAlert));

		private static readonly MethodInfo _throttleRun =
			AccessTools.Method(typeof(AlertThrottleHelper), nameof(AlertThrottleHelper.Run));

		private static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptAlertThrottle.Enabled == true;
		}

		private static IEnumerable<CodeInstruction> Transpiler(
			IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var replaced = false;

			if (_checkAddOrRemoveAlert != null && _throttleRun != null)
			{
				for (var i = 0; i < codes.Count; i++)
				{
					var code = codes[i];
					if (code.opcode != OpCodes.Call ||
						!_checkAddOrRemoveAlert.Equals(code.operand) ||
						!IsMainScanCall(codes, i))
					{
						continue;
					}

					// Keep labels and exception blocks: only the callee changes.
					code.operand = _throttleRun;
					replaced = true;
				}
			}

			if (!replaced)
			{
				YaOptMod.Warning(
					"Alert throttling could not find the AlertsReadout.AlertsReadoutUpdate scan call. " +
					"Another mod may already replace it; vanilla alert updates are left untouched.");
			}
			else
			{
				YaOptMod.Debug("Alert throttling patch applied.");
			}

			return codes;
		}

		private static bool IsMainScanCall(List<CodeInstruction> codes, int index)
		{
			// Main scan shape: this.AllAlerts[i] -> CheckAddOrRemoveAlert(alert, false).
			return index >= 4
				&& codes[index - 1].opcode == OpCodes.Ldc_I4_0
				&& codes[index - 2].opcode == OpCodes.Callvirt
				&& codes[index - 2].operand is MethodInfo getItem
				&& getItem.Name == "get_Item"
				&& codes[index - 4].LoadsField("AllAlerts", isStatic: false);
		}
	}
}