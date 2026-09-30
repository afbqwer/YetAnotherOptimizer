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
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastListerRemove"/>
	[HarmonyPatch]
	internal static class Verse_ListerThings_Remove
	{
		static MethodBase TargetMethod()
		{
			// Kingfisher pastes its own body into this method via PurePatcher's [ReplaceMethod]
			// instead of patching it, so its own "Remove" helper is never called at runtime.
			// Patch the original method so that both the vanilla and the rewritten body are handled.
			return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove));
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastListerRemove.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			if (!TryFindGroupRemove(codes, out var groupRemoveIndex, out var localThingRequestGroup))
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
				else if (IsRemove<Thing>(instruction))
				{
					// Replace
					// list.Remove(thing);
					// to
					// RemoveFromThingList(list, thing, indexer, record, use, indexType);
					yield return CodeInstruction.LoadLocal(localIndexer.LocalIndex);
					yield return CodeInstruction.LoadLocal(localRecord.LocalIndex);
					yield return CodeInstruction.LoadLocal(localUse.LocalIndex);
					if (i == groupRemoveIndex)
					{
						// indexType = thingRequestGroup
						yield return CodeInstruction.LoadLocal(localThingRequestGroup);
					}
					else
					{
						// indexType = INDEX_TYPE_DEF
						yield return new CodeInstruction(OpCodes.Ldc_I4, ListerThingsHelper.INDEX_TYPE_DEF);
					}
					yield return CodeInstruction.Call(
						typeof(ListerThingsHelper), nameof(ListerThingsHelper.RemoveFromThingList));
					if (IsVoidRemove(instruction))
						yield return new CodeInstruction(OpCodes.Pop);

					continue;
				}
				else if (IsRemove<IHaulSource>(instruction))
				{
					// Replace
					// list.Remove(haulSources);
					// to
					// RemoveFromHaulList(list, haulSources, indexer, record, use);
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
		/// Locates the removal that operates on one of the <c>listsByGroup</c> lists and the local
		/// variable used as its index.
		/// </summary>
		private static bool TryFindGroupRemove(List<CodeInstruction> instructions, out int removeIndex, out int localIndex)
		{
			removeIndex = -1;
			localIndex = -1;

			for (var i = 0; i < instructions.Count; i++)
			{
				if (!IsRemove<Thing>(instructions[i]))
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
		/// Resolves the local variable used as the index of the array element on which the removal
		/// at <paramref name="removeIndex"/> is performed.
		/// </summary>
		/// <remarks>
		/// The local index is resolved from the IL pattern instead of being hard-coded, because the
		/// method body can be rewritten by other mods (e.g. Kingfisher), which shifts local indices.
		/// The "listsByDef" removal is not indexed by an array element and is therefore rejected.
		/// </remarks>
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

		/// <summary>
		/// Checks whether the removal call returns void.
		/// </summary>
		/// <remarks>
		/// <c>List&lt;T&gt;.Remove</c> returns a boolean that the original IL pops itself, while
		/// Kingfisher's <c>RemoveFromTail</c> returns void and needs an extra pop.
		/// </remarks>
		private static bool IsVoidRemove(CodeInstruction instruction)
		{
			return instruction.operand is MethodInfo methodInfo && methodInfo.ReturnType == typeof(void);
		}

		private static bool IsRemove<T>(CodeInstruction instruction)
		{
			if (instruction.operand is MethodInfo methodInfo)
			{
				if (methodInfo.Name == "Remove")
				{
					return methodInfo.DeclaringType == typeof(List<T>);
				}
				// Compatible with Kingfisher
				if (methodInfo.Name == "RemoveFromTail")
				{
					return methodInfo.IsGenericMethod && methodInfo.GetGenericArguments()[0] == typeof(T);
				}
			}
			return false;
		}
	}
}
