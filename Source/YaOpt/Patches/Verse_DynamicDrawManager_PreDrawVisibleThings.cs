using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Uses smaller render-preparation chunks so unfinished work remains available to idle workers.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptPrepareBatchCount"/>
	[HarmonyPatch(typeof(DynamicDrawManager))]
	[HarmonyPatch("PreDrawVisibleThings")]
	internal static class Verse_DynamicDrawManager_PreDrawVisibleThings
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPrepareBatchCount.Enabled;
		}

		// Preserve the argument-consuming call shape so the scheduling stack stays balanced.
		public static int GetPreparationBatchCount(int items)
		{
			return 4;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var getIdealBatchCount = AccessTools.Method(typeof(UnityData), nameof(UnityData.GetIdealBatchCount));
			var matches = 0;
			for (var i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo method &&
					method == getIdealBatchCount)
				{
					matches++;
				}
			}

			if (matches != 1)
			{
				YaOptMod.Warning($"Render preparation batch patch expected one GetIdealBatchCount call but found {matches}; leaving vanilla scheduling unchanged.");
				for (var i = 0; i < codes.Count; i++)
					yield return codes[i];
				yield break;
			}

			for (var i = 0; i < codes.Count; i++)
			{
				var instruction = codes[i];
				if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
					method == getIdealBatchCount)
				{
					yield return new CodeInstruction(OpCodes.Call,
						AccessTools.Method(typeof(Verse_DynamicDrawManager_PreDrawVisibleThings), nameof(GetPreparationBatchCount)));
					continue;
				}
				yield return instruction;
			}
		}
	}
}
