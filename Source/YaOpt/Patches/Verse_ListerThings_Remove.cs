using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using YaOpt.Helpers;
using static YaOpt.Helpers.ListerThingsIndexer;

namespace YaOpt.Patches
{
	/// <summary>
	/// Uses the per-thing index to remove entries from the global <see cref="ListerThings"/> lists in O(1).
	/// </summary>
	/// <remarks>
	/// RimWorld 1.7 routes all list removals through <c>ListerThings.RemoveSwap</c>, which still
	/// performs a linear <c>IndexOf</c>. This patch replaces both call sites with the indexed
	/// helper, preserving swap-remove semantics.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastListerRemove"/>
	[HarmonyPatch]
	internal static class Verse_ListerThings_Remove
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove));
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastListerRemove.Enabled &&
				   ListerThingsHelper.FastRemoveAvailable;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			if (!TryLocateGroupRemove(codes, out var groupRemoveIndex, out var localThingRequestGroup))
			{
				// Falling back to vanilla is preferable to emitting invalid IL.
				YaOptMod.Warning("Cannot resolve the ThingRequestGroup index of ListerThings.Remove, " +
								 "the fast removal has been skipped.");
				return codes;
			}

			return Patch(codes, groupRemoveIndex, localThingRequestGroup, generator);
		}

		private static IEnumerable<CodeInstruction> Patch(List<CodeInstruction> codes,
			int groupRemoveIndex, int localThingRequestGroup, ILGenerator generator)
		{
			var inited = false;
			var localUse = generator.DeclareLocal(typeof(ListerThingsUse));
			var localIndexer = generator.DeclareLocal(typeof(ListerThingsIndexer));
			var localRecord = generator.DeclareLocal(typeof(ThingRecord));

			for (var i = 0; i < codes.Count; i++)
			{
				var instruction = codes[i];

				if (!inited && instruction.Calls("TryGetValue"))
				{
					inited = true;
					// var use = this.use;
					yield return CodeInstruction.LoadArgument(0);
					yield return CodeInstruction.LoadField(typeof(ListerThings), nameof(ListerThings.use));
					yield return CodeInstruction.StoreLocal(localUse.LocalIndex);
					// var indexer = ListerThingsIndexer.GetListerThingsIndex(this);
					yield return CodeInstruction.LoadArgument(0);
					yield return CodeInstruction.Call(
						typeof(ListerThingsIndexer),
						nameof(GetListerThingsIndex));
					yield return CodeInstruction.StoreLocal(localIndexer.LocalIndex);
					// var record = indexer.GetThingRecord(thing, use);
					yield return CodeInstruction.LoadLocal(localIndexer.LocalIndex);
					yield return CodeInstruction.LoadArgument(1);
					yield return CodeInstruction.LoadLocal(localUse.LocalIndex);
					yield return CodeInstruction.Call(
						typeof(ListerThingsIndexer),
						nameof(ListerThingsIndexer.GetThingRecord));
					yield return CodeInstruction.StoreLocal(localRecord.LocalIndex);
					// indexer.Remove(thing, use);
					yield return CodeInstruction.LoadLocal(localIndexer.LocalIndex);
					yield return CodeInstruction.LoadArgument(1);
					yield return CodeInstruction.LoadLocal(localUse.LocalIndex);
					yield return CodeInstruction.Call(
						typeof(ListerThingsIndexer),
						nameof(ListerThingsIndexer.Remove));
				}
				else if (IsRemoveSwap(instruction))
				{
					// Replace
					// RemoveSwap(list, thing);
					// to
					// RemoveFromThingList(list, thing, indexer, record, use, indexType);
					yield return CodeInstruction.LoadLocal(localIndexer.LocalIndex);
					yield return CodeInstruction.LoadLocal(localRecord.LocalIndex);
					yield return CodeInstruction.LoadLocal(localUse.LocalIndex);
					if (i == groupRemoveIndex)
					{
						// indexType = loop index (equal to the ThingRequestGroup enum value)
						yield return CodeInstruction.LoadLocal(localThingRequestGroup);
					}
					else
					{
						// indexType = INDEX_TYPE_DEF
						yield return new CodeInstruction(OpCodes.Ldc_I4, ListerThingsHelper.INDEX_TYPE_DEF);
					}
					yield return CodeInstruction.Call(
						typeof(ListerThingsHelper), nameof(ListerThingsHelper.RemoveFromThingList));
					// RemoveFromThingList returns bool, RemoveSwap returns void.
					yield return new CodeInstruction(OpCodes.Pop);
					continue;
				}
				else if (IsRemoveHaulSource(instruction))
				{
					// Replace
					// haulSources.Remove(haulSource);
					// to
					// RemoveFromHaulList(haulSources, haulSource, indexer, record, use);
					yield return CodeInstruction.LoadLocal(localIndexer.LocalIndex);
					yield return CodeInstruction.LoadLocal(localRecord.LocalIndex);
					yield return CodeInstruction.LoadLocal(localUse.LocalIndex);
					yield return CodeInstruction.Call(
						typeof(ListerThingsHelper), nameof(ListerThingsHelper.RemoveFromHaulList));
					if (IsVoidRemove(instruction))
						yield return new CodeInstruction(OpCodes.Pop);
					continue;
				}

				yield return instruction;
			}
		}

		/// <summary>
		/// Locates the group-list removal and the local variable used as its array index.
		/// </summary>
		/// <remarks>
		/// Also used by <see cref="ListerThingsHelper.EvaluateFastRemoveAvailability"/> as the
		/// pre-flight check, so that every patch depending on the thing index is decided before
		/// any of them is applied.
		/// </remarks>
		internal static bool TryLocateGroupRemove(List<CodeInstruction> instructions, out int removeIndex, out int localIndex)
		{
			removeIndex = -1;
			localIndex = -1;

			for (var i = 0; i < instructions.Count; i++)
			{
				if (!IsRemoveSwap(instructions[i]))
					continue;
				localIndex = FindListIndexLocal(instructions, i);
				if (localIndex < 0)
					continue;
				removeIndex = i;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Checks whether the group removal site exists, without resolving its details.
		/// </summary>
		internal static bool TryLocateGroupRemove(List<CodeInstruction> instructions)
		{
			return TryLocateGroupRemove(instructions, out _, out _);
		}

		/// <summary>
		/// Resolves the local variable used as the index of the array element on which the removal
		/// at <paramref name="removeIndex"/> is performed.
		/// </summary>
		private static int FindListIndexLocal(List<CodeInstruction> instructions, int removeIndex)
		{
			for (var i = removeIndex - 1; i >= 0 && i >= removeIndex - 8; i--)
			{
				if (instructions[i].opcode != OpCodes.Ldelem_Ref)
					continue;

				for (var j = i - 1; j >= 0 && j >= i - 3; j--)
				{
					if (instructions[j].IsLdloc())
						return instructions[j].LocalIndex();
				}
				break;
			}
			return -1;
		}

		private static bool IsRemoveSwap(CodeInstruction instruction)
		{
			return instruction.opcode == OpCodes.Call &&
				   instruction.operand is MethodInfo methodInfo &&
				   methodInfo.Name == "RemoveSwap" &&
				   methodInfo.DeclaringType == typeof(ListerThings);
		}

		/// <summary>
		/// Checks whether the removal call returns void.
		/// </summary>
		private static bool IsVoidRemove(CodeInstruction instruction)
		{
			return instruction.operand is MethodInfo methodInfo && methodInfo.ReturnType == typeof(void);
		}

		private static bool IsRemoveHaulSource(CodeInstruction instruction)
		{
			return instruction.operand is MethodInfo methodInfo &&
				   methodInfo.Name == "Remove" &&
				   methodInfo.DeclaringType == typeof(List<IHaulSource>);
		}
	}
}