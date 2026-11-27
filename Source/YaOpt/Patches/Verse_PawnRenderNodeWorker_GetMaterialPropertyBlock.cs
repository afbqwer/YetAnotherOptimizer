using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Returns the node's pre-filled material property block for vanilla material paths.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	[HarmonyPatch(typeof(PawnRenderNodeWorker))]
	[HarmonyPatch(nameof(PawnRenderNodeWorker.GetMaterialPropertyBlock))]
	internal static class Verse_PawnRenderNodeWorker_GetMaterialPropertyBlock
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var label = generator.DefineLabel();
			yield return CodeInstruction.LoadArgument(1);
			yield return CodeInstruction.LoadArgument(2);
			yield return CodeInstruction.LoadArgument(3);
			yield return CodeInstruction.Call(typeof(ParallelPreDrawHelper),
				nameof(ParallelPreDrawHelper.CanUsePreparedBlock));
			yield return new CodeInstruction(OpCodes.Brfalse_S, label);
			yield return CodeInstruction.LoadArgument(1);
			yield return new CodeInstruction(OpCodes.Callvirt,
				AccessTools.PropertyGetter(typeof(PawnRenderNode), nameof(PawnRenderNode.MatPropBlock)));
			yield return new CodeInstruction(OpCodes.Ret);
			yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
			foreach (var instruction in instructions)
				yield return instruction;
		}
	}
}
