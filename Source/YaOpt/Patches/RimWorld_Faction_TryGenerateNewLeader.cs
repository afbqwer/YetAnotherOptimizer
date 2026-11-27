using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Returns from faction leader generation when vanilla produced no pawn.
	/// </summary>
	/// <remarks>
	/// <c>TryGenerateNewLeader</c> assigns <c>PawnGenerator.GeneratePawn</c>'s result to the leader field
	/// and immediately reads <c>leader.RaceProps</c> without a null check. <c>GeneratePawn</c> returns
	/// null once its 120 attempts are exhausted, for example when a validator rejects every candidate.
	/// That null dereference aborts the whole world-generation faction step. The inserted guard only
	/// runs on the null path and otherwise leaves the original method untouched.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationFailureGuard"/>
	[HarmonyPatch(typeof(Faction), nameof(Faction.TryGenerateNewLeader))]
	internal static class RimWorld_Faction_TryGenerateNewLeader
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGenerationFailureGuard.Enabled == true
				&& AccessTools.Method(typeof(Faction), nameof(Faction.TryGenerateNewLeader)) != null;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
			ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count - 2; i++)
			{
				if (!IsGeneratePawnCall(codes[i]))
					continue;

				var storeIndex = i + 1;
				if (!storesLeader(codes[storeIndex]))
					return codes;

				var guard = generator.DefineLabel();
				codes[storeIndex + 1].labels.Add(guard);

				// if (leader == null) return false;
				codes.Insert(storeIndex + 1, new CodeInstruction(OpCodes.Ret));
				codes.Insert(storeIndex + 1, new CodeInstruction(OpCodes.Ldc_I4_0));
				codes.Insert(storeIndex + 1, new CodeInstruction(OpCodes.Brtrue, guard));
				codes.Insert(storeIndex + 1, CodeInstruction.LoadField(typeof(Faction), nameof(Faction.leader)));
				codes.Insert(storeIndex + 1, CodeInstruction.LoadArgument(0));
				return codes;
			}

			return codes;
		}

		private static bool IsGeneratePawnCall(CodeInstruction code)
		{
			if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method))
				return false;
			if (method.Name != nameof(PawnGenerator.GeneratePawn)
				|| method.DeclaringType != typeof(PawnGenerator))
				return false;

			var parameters = method.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType == typeof(PawnGenerationRequest);
		}

		private static bool storesLeader(CodeInstruction code)
		{
			return code.opcode == OpCodes.Stfld
				&& code.operand is FieldInfo field
				&& field.Name == nameof(Faction.leader)
				&& field.DeclaringType == typeof(Faction);
		}
	}
}