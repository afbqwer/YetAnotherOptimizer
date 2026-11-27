using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <c>Enum.HasFlag</c> on <see cref="FoodTypeFlags"/> with a plain bit test.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>x.HasFlag(f)</c> compiles to <c>box FoodTypeFlags; ldc.i4 f; box FoodTypeFlags; call
	/// Enum::HasFlag</c>: the receiver of an instance method on <see cref="Enum"/> has to be boxed as
	/// well as the argument, so every call allocates two 24-byte objects.
	/// </para>
	/// <para>
	/// An optimized JIT folds that pattern into <c>(x &amp; f) == f</c> and drops both boxes, but the
	/// intrinsic only exists in optimized code. Methods that never leave tier 0 - including bodies
	/// rewritten by a Harmony transpiler - keep allocating. Rewriting the IL removes the dependency
	/// on that decision; the replacement is the exact definition of <c>HasFlag</c>, which is what
	/// <see cref="FoodTypeFlags.Fungus"/> (<c>0x1001</c>, a composite value) requires: a
	/// non-zero test would answer true for a def that only has the vegetable bit.
	/// </para>
	/// <para>
	/// <see cref="FoodUtility.GetFoodKind(ThingDef)"/> is also transpiled by other mods, so the
	/// rewrite is per call site and only replaces the four-instruction window it recognizes. Not
	/// finding it leaves the method untouched instead of partially rewriting it.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptEnumHasFlagBitTest"/>
	[HarmonyPatch]
	internal static class MultiTargets_EnumHasFlag
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var processed = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsProcessedFood));
			if (processed != null)
				yield return processed;

			var animalProduct = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsAnimalProduct));
			if (animalProduct != null)
				yield return animalProduct;

			var fungus = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsFungus));
			if (fungus != null)
				yield return fungus;

			var foodKind = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.GetFoodKind), new[] { typeof(ThingDef) });
			if (foodKind != null)
				yield return foodKind;
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptEnumHasFlagBitTest.Enabled == true;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);

			var rewritten = new List<CodeInstruction>(list.Count + 8);
			var changed = false;
			for (var i = 0; i < list.Count; i++)
			{
				if (i + 3 < list.Count &&
					IsFoodTypeFlagsBox(list[i]) &&
					TryGetIntConstant(list[i + 1], out var flag) &&
					IsFoodTypeFlagsBox(list[i + 2]) &&
					IsHasFlagCall(list[i + 3]))
				{
					// Dropping both boxes leaves the raw flag value on the stack, so the flag has to be
					// pushed again to compare against.
					var first = new CodeInstruction(OpCodes.Ldc_I4, flag);
					first.labels.AddRange(list[i].labels);
					first.blocks.AddRange(list[i].blocks);
					first.labels.AddRange(list[i + 1].labels);
					first.blocks.AddRange(list[i + 1].blocks);
					rewritten.Add(first);
					rewritten.Add(new CodeInstruction(OpCodes.And));
					rewritten.Add(new CodeInstruction(OpCodes.Ldc_I4, flag));

					var compare = new CodeInstruction(OpCodes.Ceq);
					compare.labels.AddRange(list[i + 3].labels);
					compare.blocks.AddRange(list[i + 3].blocks);
					rewritten.Add(compare);

					changed = true;
					i += 3;
					continue;
				}

				rewritten.Add(list[i]);
			}

			return changed ? rewritten : list;
		}

		private static bool IsFoodTypeFlagsBox(CodeInstruction instruction)
		{
			if (instruction.opcode != OpCodes.Box)
				return false;
			if (Equals(instruction.operand, typeof(FoodTypeFlags)))
				return true;

			// Fallback for a load context that hands back a different Type instance for the same def.
			return instruction.operand is Type type && type.FullName == "RimWorld.FoodTypeFlags";
		}

		private static bool IsHasFlagCall(CodeInstruction instruction)
		{
			if (instruction.opcode != OpCodes.Call || !(instruction.operand is MethodInfo method))
				return false;
			return method.Name == nameof(Enum.HasFlag) &&
				method.DeclaringType?.FullName == "System.Enum" &&
				method.GetParameters().Length == 1;
		}

		private static bool TryGetIntConstant(CodeInstruction instruction, out int value)
		{
			if (instruction.opcode == OpCodes.Ldc_I4)
			{
				value = Convert.ToInt32(instruction.operand);
				return true;
			}

			if (instruction.opcode == OpCodes.Ldc_I4_S)
			{
				value = Convert.ToInt32(instruction.operand);
				return true;
			}

			if (instruction.opcode == OpCodes.Ldc_I4_M1)
			{
				value = -1;
				return true;
			}

			value = 0;
			if (instruction.opcode == OpCodes.Ldc_I4_0) return true;
			if (instruction.opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
			if (instruction.opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }
			return false;
		}
	}
}
