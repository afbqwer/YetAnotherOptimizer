using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Returns the property block prepared in parallel and resets the per-request marker.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	[HarmonyPatch(typeof(PawnRenderNodeWorker))]
	[HarmonyPatch(nameof(PawnRenderNodeWorker.GetMaterialPropertyBlock))]
	internal static class Verse_PawnRenderNodeWorker_GetMaterialPropertyBlock
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
			yield return CodeInstruction.LoadArgument(0);
			yield return CodeInstruction.LoadArgument(1);
			yield return CodeInstruction.Call(typeof(ParallelPreDrawHelper), nameof(ParallelPreDrawHelper.ClearMaterialPrepared));
			yield return CodeInstruction.LoadArgument(1);
			yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PawnRenderNode), nameof(PawnRenderNode.MatPropBlock)));
			yield return new CodeInstruction(OpCodes.Ret);
			yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
			foreach (var instruction in instructions)
				yield return instruction;
		}
	}
}
