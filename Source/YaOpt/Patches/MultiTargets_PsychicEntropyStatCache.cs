using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes neural heat maximum lookups through the vanilla temporary stat cache.
	/// </summary>
	/// <remarks>
	/// The matching XML patch marks PsychicEntropyMax cacheable. Vanilla then reuses the value for
	/// 20 ticks instead of evaluating the full stat at every getter call.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPsychicEntropyCache"/>
	internal static class MultiTargets_PsychicEntropyStatCache
	{
		private const int CacheInterval = 20;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.PropertyGetter(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.MaxEntropy));
			yield return AccessTools.PropertyGetter(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.MaxPotentialEntropy));
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPsychicEntropyCache.Enabled == true;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);
			var getStatValue = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue),
				new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
			if (getStatValue == null)
				return list;

			var replacements = 0;
			for (var i = 1; i < list.Count; i++)
			{
				if (list[i].opcode != OpCodes.Call || !Equals(list[i].operand, getStatValue))
					continue;

				var interval = list[i - 1];
				var isMinusOne = interval.opcode == OpCodes.Ldc_I4_M1
					|| (interval.opcode == OpCodes.Ldc_I4 && Equals(interval.operand, -1));
				if (!isMinusOne)
					continue;

				interval.opcode = OpCodes.Ldc_I4;
				interval.operand = CacheInterval;
				replacements++;
			}

			// A vanilla or modded IL change must leave the getter untouched rather than partially patched.
			return replacements == 1 ? list : new List<CodeInstruction>(instructions);
		}
	}
}
