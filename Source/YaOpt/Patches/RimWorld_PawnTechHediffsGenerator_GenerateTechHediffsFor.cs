using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips a tech hediff pick when the candidate pool has no positive weight.
	/// </summary>
	/// <remarks>
	/// Vanilla calls <c>GenCollection.RandomElementByWeight</c> and immediately reads
	/// <c>BaseMarketValue</c> from the result. That method returns null (after logging) when every
	/// candidate costs zero, so the dereference throws and <c>GeneratePawn</c> rethrows it into the
	/// world generation step, which then drops the whole faction layer. The replacement keeps the
	/// original weighted call and its random consumption, and only adds a null check that jumps to
	/// the loop increment.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationFailureGuard"/>
	/// <seealso cref="TechHediffFallback"/>
	[HarmonyPatch(typeof(PawnTechHediffsGenerator),
		nameof(PawnTechHediffsGenerator.GenerateTechHediffsFor))]
	internal static class RimWorld_PawnTechHediffsGenerator_GenerateTechHediffsFor
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGenerationFailureGuard.Enabled == true
				&& AccessTools.Method(typeof(PawnTechHediffsGenerator),
					nameof(PawnTechHediffsGenerator.GenerateTechHediffsFor)) != null;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
			ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			ReplaceMarketValueCalls(codes);

			var callIndex = FindWeightedCall(codes);
			if (callIndex < 0 || callIndex + 1 >= codes.Count)
				return codes;

			var storeIndex = callIndex + 1;
			var store = codes[storeIndex];
			var localIndex = store.LocalIndex();
			if (!IsLocalStore(store.opcode) || localIndex < 0)
				return codes;

			var continueIndex = FindLoopContinue(codes, callIndex);
			if (continueIndex < 0)
				return codes;

			var skip = generator.DefineLabel();
			codes[continueIndex].labels.Add(skip);

			var pick = AccessTools.Method(typeof(TechHediffFallback),
				nameof(TechHediffFallback.PickOrNull));
			var replacement = new CodeInstruction(OpCodes.Call, pick);
			replacement.labels.AddRange(codes[callIndex].labels);
			replacement.blocks.AddRange(codes[callIndex].blocks);
			codes[callIndex] = replacement;

			// if (thingDef == null) continue; the weighted call consumed no random number because the
			// total weight was zero, so the stream still matches vanilla.
			codes.Insert(storeIndex + 1, new CodeInstruction(OpCodes.Brfalse, skip));
			codes.Insert(storeIndex + 1, CodeInstruction.LoadLocal(localIndex));
			return codes;
		}

		/// <summary>
		/// Reads market value through a null-safe helper so a null entry in
		/// <c>techHediffsRequired</c> costs no money instead of throwing.
		/// </summary>
		private static void ReplaceMarketValueCalls(List<CodeInstruction> codes)
		{
			var marketValue = AccessTools.Method(typeof(TechHediffFallback),
				nameof(TechHediffFallback.MarketValue));
			var getterName = "get_" + nameof(ThingDef.BaseMarketValue);
			for (var i = 0; i < codes.Count; i++)
			{
				var code = codes[i];
				if (code.opcode != OpCodes.Callvirt || !(code.operand is MethodInfo method))
					continue;
				if (method.Name != getterName || method.DeclaringType != typeof(ThingDef))
					continue;

				var replacement = new CodeInstruction(OpCodes.Call, marketValue);
				replacement.labels.AddRange(code.labels);
				replacement.blocks.AddRange(code.blocks);
				codes[i] = replacement;
			}
		}

		private static int FindWeightedCall(List<CodeInstruction> codes)
		{
			for (var i = 0; i < codes.Count; i++)
			{
				var code = codes[i];
				if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method))
					continue;
				if (method.Name != nameof(GenCollection.RandomElementByWeight)
					|| method.DeclaringType != typeof(GenCollection)
					|| !method.IsGenericMethod
					|| method.GetGenericArguments()[0] != typeof(ThingDef))
					continue;

				return i;
			}

			return -1;
		}

		/// <summary>
		/// Finds the loop increment the chance check branches to. Reusing that shape as the skip
		/// target keeps us out of unrelated code when another transpiler changed the body.
		/// </summary>
		private static int FindLoopContinue(List<CodeInstruction> codes, int callIndex)
		{
			var branchTargets = new HashSet<Label>();
			for (var i = 0; i < callIndex; i++)
			{
				if (IsBranch(codes[i].opcode) && codes[i].operand is Label label)
					branchTargets.Add(label);
			}

			if (branchTargets.Count == 0)
				return -1;

			for (var i = callIndex + 2; i < codes.Count; i++)
			{
				var labels = codes[i].labels;
				for (var j = 0; j < labels.Count; j++)
				{
					if (branchTargets.Contains(labels[j]))
						return i;
				}
			}

			return -1;
		}

		private static bool IsLocalStore(OpCode opcode)
		{
			return opcode == OpCodes.Stloc
				|| opcode == OpCodes.Stloc_S
				|| opcode == OpCodes.Stloc_0
				|| opcode == OpCodes.Stloc_1
				|| opcode == OpCodes.Stloc_2
				|| opcode == OpCodes.Stloc_3;
		}

		private static bool IsBranch(OpCode opcode)
		{
			return opcode == OpCodes.Br
				|| opcode == OpCodes.Br_S
				|| opcode == OpCodes.Brfalse
				|| opcode == OpCodes.Brfalse_S
				|| opcode == OpCodes.Brtrue
				|| opcode == OpCodes.Brtrue_S
				|| opcode == OpCodes.Beq
				|| opcode == OpCodes.Beq_S
				|| opcode == OpCodes.Bge
				|| opcode == OpCodes.Bge_S
				|| opcode == OpCodes.Bgt
				|| opcode == OpCodes.Bgt_S
				|| opcode == OpCodes.Ble
				|| opcode == OpCodes.Ble_S
				|| opcode == OpCodes.Blt
				|| opcode == OpCodes.Blt_S
				|| opcode == OpCodes.Bne_Un
				|| opcode == OpCodes.Bne_Un_S
				|| opcode == OpCodes.Bge_Un
				|| opcode == OpCodes.Bge_Un_S
				|| opcode == OpCodes.Bgt_Un
				|| opcode == OpCodes.Bgt_Un_S
				|| opcode == OpCodes.Ble_Un
				|| opcode == OpCodes.Ble_Un_S
				|| opcode == OpCodes.Blt_Un
				|| opcode == OpCodes.Blt_Un_S
				|| opcode == OpCodes.Leave
				|| opcode == OpCodes.Leave_S;
		}
	}
}