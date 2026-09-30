using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using YaOpt.Helpers.ThreadLocal;

namespace YaOpt.Patches.ThreadSafe.ThreadLocal
{
	[HarmonyPatch]
	[HarmonyPriority(Priority.VeryLow)]
	internal static class MultiTargets_ListerBuildings
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(
				typeof(ListerBuildings),
				nameof(ListerBuildings.AllBuildingsColonistOfDef));
			yield return AccessTools.Method(
				typeof(ListerBuildings),
				nameof(ListerBuildings.AllBuildingsColonistOfGroup));
		}

		static bool Prepare()
		{
			return YaOptGlobal.NeedThreadSafe || YaOptGlobal.Settings.OptParallelWorkGiver.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
			ILGenerator generator, MethodBase method)
		{
			var local = generator.DeclareLocal(typeof(List<Building>));
			var tlaType = typeof(ThreadLocalAllocator<List<Building>>);
			int key = -1;
			if (method.Name == "AllBuildingsColonistOfDef")
			{
				key = ThreadLocalAllocator<List<Building>>.TryAllocate(
					"___ListerBuildings_allBuildingsColonistOfDefResult");
			}
			else if (method.Name == "AllBuildingsColonistOfGroup")
			{
				key = ThreadLocalAllocator<List<Building>>.TryAllocate(
					"___ListerBuildings_allBuildingsColonistOfGroupResult");
			}
			else
			{
				throw new Exception($"Unknown method: {method.FullName()}");
			}
			yield return new CodeInstruction(OpCodes.Ldc_I4, key);
			yield return CodeInstruction.Call(tlaType, "Get");
			yield return CodeInstruction.StoreLocal(local.LocalIndex);
			foreach (var instruction in instructions)
			{
				// Replace listerBuildings.ColonistBuildingsOfDefResult with the thread local.
				// Kingfisher reaches this buffer through its own accessor instead of the vanilla field,
				// and copies its body into the vanilla method via PurePatcher's [ReplaceMethod].
				// The replacement has the same stack effect as the accessor call, so it stays valid
				// in any context.
				if (instruction.Calls("ColonistBuildingsOfDefResult"))
				{
					yield return new CodeInstruction(OpCodes.Pop);
					yield return CodeInstruction.LoadLocal(local.LocalIndex);
					continue;
				}
				// Replace listerBuildings.allBuildingsColonistOfDefResult with the thread local
				if (instruction.LoadsField("allBuildingsColonistOfDefResult", true))
				{
					yield return CodeInstruction.LoadLocal(local.LocalIndex);
					continue;
				}
				// Replace listerBuildings.allBuildingsColonistOfGroupResult with the thread local
				if (instruction.LoadsField("allBuildingsColonistOfGroupResult", true))
				{
					yield return CodeInstruction.LoadLocal(local.LocalIndex);
					continue;
				}
				yield return instruction;
			}
		}
	}
}