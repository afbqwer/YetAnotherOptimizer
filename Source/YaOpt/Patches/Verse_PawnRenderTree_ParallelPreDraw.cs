using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces recursive render-node recache checks with the compatibility-aware fast path.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastRecacheRequested"/>
	[HarmonyPatch(typeof(PawnRenderTree))]
	[HarmonyPatch(nameof(PawnRenderTree.ParallelPreDraw))]
	internal static class Verse_PawnRenderTree_ParallelPreDraw
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastRecacheRequested.Enabled ||
				   (YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled && PrepatcherState.ParallelMaterialUpdateActive);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var fastRecache = YaOptGlobal.Settings.OptFastRecacheRequested.Enabled;
			foreach (var instruction in instructions)
			{
				if (fastRecache && instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo methodInfo &&
					methodInfo.Name == "get_RecacheRequested")
				{
					yield return CodeInstruction.Call(
						typeof(ParallelPreDrawHelper),
						nameof(ParallelPreDrawHelper.FastRecacheRequested));
					continue;
				}
				yield return instruction;
			}
		}

		static void Postfix(PawnDrawParms parms, List<PawnGraphicDrawRequest> ___drawRequests)
		{
			if (!YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled || !PrepatcherState.ParallelMaterialUpdateActive)
				return;

			for (var i = 0; i < ___drawRequests.Count; i++)
			{
				var request = ___drawRequests[i];
				ParallelPreDrawHelper.PrepareMaterialPropertyBlock(request.node, request.material, parms);
			}
		}
	}
}
