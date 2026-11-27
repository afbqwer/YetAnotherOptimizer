using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	[HarmonyPatch(typeof(JobGiver_BoardOrLeaveGravship), "TryGiveJob")]
	internal static class RimWorld_JobGiver_BoardOrLeaveGravship_TryGiveJob
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptGravshipJobFastExit.Enabled == true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static Thing FirstOrNull(List<Thing> things)
		{
			return things.Count == 0 ? null : things[0];
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			CodeInstruction previous = null;
			foreach (var instruction in instructions)
			{
				if (CanReplace(instruction, previous))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(
						typeof(RimWorld_JobGiver_BoardOrLeaveGravship_TryGiveJob),
						nameof(FirstOrNull));
				}

				previous = instruction;
				yield return instruction;
			}
		}

		private static bool CanReplace(CodeInstruction instruction, CodeInstruction previous)
		{
			if (!instruction.Calls(nameof(Enumerable.FirstOrDefault)) ||
				!(instruction.operand is MethodInfo method) ||
				method.DeclaringType != typeof(Enumerable) ||
				!method.IsGenericMethod || method.GetGenericArguments()[0] != typeof(Thing))
			{
				return false;
			}

			return previous?.operand is MethodInfo provider &&
				provider.ReturnType == typeof(List<Thing>);
		}
	}
}
