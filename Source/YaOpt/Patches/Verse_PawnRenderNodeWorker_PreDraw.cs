using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips the base color write when parallel preparation already filled the node block.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	[HarmonyPatch(typeof(PawnRenderNodeWorker))]
	[HarmonyPatch(nameof(PawnRenderNodeWorker.PreDraw))]
	internal static class Verse_PawnRenderNodeWorker_PreDraw
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled &&
				PrepatcherState.PreparedMaterialActive;
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
			yield return new CodeInstruction(OpCodes.Ret);
			yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
			foreach (var instruction in instructions)
				yield return instruction;
		}
	}
}
