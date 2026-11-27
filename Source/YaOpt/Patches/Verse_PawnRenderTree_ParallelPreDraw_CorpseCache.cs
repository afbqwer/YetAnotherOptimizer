using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reuses a dead pawn's prepared draw requests and matrices while its draw inputs stay unchanged.
	/// </summary>
	/// <remarks>
	/// The transpiler records the completed vanilla run, so a later prefix that skips the original
	/// cannot make the cache look fresh. Cached frames still run the material postfix, which refills
	/// the block that the previous draw cleared.
	/// </remarks>
	[HarmonyPatch(typeof(PawnRenderTree))]
	[HarmonyPatch(nameof(PawnRenderTree.ParallelPreDraw))]
	internal static class Verse_PawnRenderTree_ParallelPreDraw_CorpseCache
	{
		private const int RefreshEvery = 30;

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptReuseCorpseRender.Enabled &&
				PrepatcherState.CorpseRenderActive;
		}

		static bool Prefix(PawnRenderTree __instance, PawnDrawParms parms)
		{
			if (!IsCacheable(__instance, parms))
				return true;

			ref var state = ref __instance.CorpseRender();
			if (!state.Valid || state.HasPerRequestMaterial || state.Seen >= RefreshEvery)
				return true;

			if (state.Parms.ShouldRecache(parms) || state.Parms.matrix != parms.matrix ||
				state.Parms.tint != parms.tint || __instance.rootNode.RecacheRequested)
				return true;

			state.Seen++;
			return false;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var record = AccessTools.Method(typeof(Verse_PawnRenderTree_ParallelPreDraw_CorpseCache),
				nameof(Record));
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ret && record != null)
				{
					var first = new CodeInstruction(OpCodes.Ldarg_0);
					first.labels.AddRange(instruction.labels);
					instruction.labels.Clear();
					first.blocks.AddRange(instruction.blocks);
					instruction.blocks.Clear();
					yield return first;
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, record);
				}

				yield return instruction;
			}
		}

		public static void Record(PawnRenderTree tree, PawnDrawParms parms)
		{
			if (!IsCacheable(tree, parms))
			{
				if (tree != null && PrepatcherState.CorpseRenderActive)
					tree.CorpseRender().Valid = false;
				return;
			}

			var hasPerRequestMaterial = false;
			var requests = tree.drawRequests;
			for (var i = 0; i < requests.Count; i++)
			{
				if (requests[i].node.CheckMaterialEveryDrawRequest)
				{
					hasPerRequestMaterial = true;
					break;
				}
			}

			ref var state = ref tree.CorpseRender();
			state.Parms = parms;
			state.Seen = 0;
			state.HasPerRequestMaterial = hasPerRequestMaterial;
			state.Valid = true;
		}

		private static bool IsCacheable(PawnRenderTree tree, PawnDrawParms parms)
		{
			return tree != null && tree.pawn != null && parms.dead &&
				tree.HasValidPropsAndRenderTree && tree.rootNode != null &&
				tree.drawRequests.Count > 0 && tree.currentAnimation == null &&
				!tree.debugTint.HasValue;
		}
	}
}