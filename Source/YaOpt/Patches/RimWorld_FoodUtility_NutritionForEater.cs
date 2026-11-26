using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes the <see cref="StatDefOf.Nutrition"/> lookup in
	/// <see cref="FoodUtility.NutritionForEater"/> through the vanilla temporary stat cache with a
	/// 20-tick interval.
	/// </summary>
	/// <remarks>
	/// Every AI food search calls <see cref="FoodUtility.NutritionForEater"/> for each candidate
	/// food, and vanilla re-evaluates the full Nutrition stat pipeline every time. Nutrition is
	/// already cacheable in vanilla, so no XML patch is needed. The second lookup in the method is
	/// Biotech's <see cref="StatDefOf.RawNutritionFactor"/> on the eater; that one is anchored away
	/// and left untouched. A dynamic nutrition change may take up to 20 ticks to affect food choice.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptNutritionStatCache"/>
	[HarmonyPatch(typeof(FoodUtility))]
	[HarmonyPatch(nameof(FoodUtility.NutritionForEater), typeof(Pawn), typeof(Thing))]
	internal static class RimWorld_FoodUtility_NutritionForEater
	{
		private const int CacheInterval = 20;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptNutritionStatCache.Enabled == true;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);
			var nutritionField = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.Nutrition));
			var getStatValue = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue),
				new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
			if (nutritionField == null || getStatValue == null)
				return list;

			var targetIndex = -1;
			for (var i = 3; i < list.Count; i++)
			{
				if (list[i].opcode != OpCodes.Call || !Equals(list[i].operand, getStatValue))
					continue;
				if (!IsMinusOne(list[i - 1]))
					continue;
				if (list[i - 2].opcode != OpCodes.Ldc_I4_1)
					continue;
				if (list[i - 3].opcode != OpCodes.Ldsfld || !Equals(list[i - 3].operand, nutritionField))
					continue;

				// Exactly one Nutrition lookup must match the vanilla shape. More than one means a
				// foreign IL change, so the method is left untouched instead of partially patched.
				if (targetIndex != -1)
					return list;

				targetIndex = i - 1;
			}

			if (targetIndex < 0)
				return list;

			list[targetIndex].opcode = OpCodes.Ldc_I4;
			list[targetIndex].operand = CacheInterval;
			return list;
		}

		private static bool IsMinusOne(CodeInstruction instruction)
		{
			if (instruction.opcode == OpCodes.Ldc_I4_M1)
				return true;
			if (instruction.opcode == OpCodes.Ldc_I4_S)
				return instruction.operand is sbyte shortValue && shortValue == -1;
			return instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int intValue && intValue == -1;
		}
	}
}