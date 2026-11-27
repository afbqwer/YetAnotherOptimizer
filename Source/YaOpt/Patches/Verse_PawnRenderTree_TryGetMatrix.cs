using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PawnRenderTree.ComputeMatrix"/> with the fused SIMD implementation.
	/// </summary>
	/// <remarks>
	/// The replacement is a static method with the receiver as its first parameter, so only the call
	/// operand changes and the argument stack stays valid. The target call is the only one in
	/// <see cref="PawnRenderTree.TryGetMatrix"/>; if that shape is missing the instructions are left
	/// untouched.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptComputeMatrixBurst"/>
	[HarmonyPatch(typeof(PawnRenderTree))]
	[HarmonyPatch(nameof(PawnRenderTree.TryGetMatrix))]
	internal static class Verse_PawnRenderTree_TryGetMatrix
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptComputeMatrixBurst.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = AccessTools.Method(
				typeof(PawnMatrixHelper),
				nameof(PawnMatrixHelper.ComputeMatrix));
			if (replacement == null)
			{
				foreach (var instruction in instructions)
					yield return instruction;
				yield break;
			}

			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo &&
					methodInfo.DeclaringType == typeof(PawnRenderTree) && methodInfo.Name == "ComputeMatrix")
				{
					instruction.operand = replacement;
				}
				yield return instruction;
			}
		}
	}
}