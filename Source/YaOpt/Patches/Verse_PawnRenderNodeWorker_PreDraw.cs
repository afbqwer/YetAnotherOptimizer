using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips the base pre-draw color write when the parallel preparation already supplied it.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	[HarmonyPatch(typeof(PawnRenderNodeWorker))]
	[HarmonyPatch(nameof(PawnRenderNodeWorker.PreDraw))]
	internal static class Verse_PawnRenderNodeWorker_PreDraw
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled && PrepatcherState.ParallelMaterialUpdateActive;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var label = generator.DefineLabel();
			yield return CodeInstruction.LoadArgument(0);
			yield return CodeInstruction.LoadArgument(1);
			yield return CodeInstruction.Call(typeof(ParallelPreDrawHelper), nameof(ParallelPreDrawHelper.IsMaterialPrepared));
			yield return new CodeInstruction(OpCodes.Brfalse_S, label);
			yield return new CodeInstruction(OpCodes.Ret);
			yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
			foreach (var instruction in instructions)
				yield return instruction;
		}
	}
}
