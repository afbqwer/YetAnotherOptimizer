using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Replaces SelectNodes with optimized enumerator for multi-node patch operations.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastPatchOperation"/>
	[HarmonyPatch]
	[EarlyPatch]
	internal static class MultiTargets_PatchOperationMulti
	{
		/// <summary>
		/// <c>ApplyWorker</c> is <c>protected</c> on <see cref="PatchOperation"/> and on every override
		/// of it. Virtual members are deliberately excluded from publicization, because a publicized
		/// virtual slot lets a bad <c>public override</c> compile and then fail to load at runtime, so
		/// <c>nameof</c> cannot reach it. The name is resolved by reflection in
		/// <see cref="AccessTools.Method(Type, string)"/>, which ignores accessibility.
		/// </summary>
		private const string ApplyWorker = "ApplyWorker";

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(PatchOperationAdd), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationAddModExtension), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationAttributeAdd), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationAttributeRemove), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationAttributeSet), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationInsert), ApplyWorker);
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastPatchOperation.Enabled ||
				   YaOptGlobal.Settings.OptFastDefPredicateXPath.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = instructions as List<CodeInstruction> ?? new List<CodeInstruction>(instructions);
			var remove = new HashSet<int>();
			for (var i = 0; i < list.Count; i++)
			{
				if (!list[i].Calls("SelectNodes"))
					continue;
				if (!TryFindListEnumerator(list, i, out var enumeratorIndex))
					return list;
				remove.Add(enumeratorIndex);
			}

			var result = new List<CodeInstruction>(list.Count);
			for (var i = 0; i < list.Count; i++)
			{
				if (remove.Contains(i))
					continue;

				var instruction = list[i];
				if (instruction.Calls("SelectNodes"))
				{
					var replacement = CodeInstruction.Call(typeof(XPathReducer),
						nameof(XPathReducer.GetXmlEnumerator));
					replacement.labels.AddRange(instruction.labels);
					replacement.blocks.AddRange(instruction.blocks);
					result.Add(replacement);
					continue;
				}
				result.Add(instruction);
			}
			return result;
		}

		/// <summary>
		/// Finds the <c>XmlNodeList.GetEnumerator</c> that consumes the list produced by the
		/// <c>SelectNodes</c> at <paramref name="selectIndex"/>.
		/// </summary>
		/// <remarks>
		/// Only nops may appear between the pair. Any other call means a foreign transpiler interleaved
		/// its own code, and rewriting then would corrupt it, so the caller leaves the body untouched.
		/// A labelled enumerator call is also refused, because removing it would drop a jump target.
		/// </remarks>
		private static bool TryFindListEnumerator(List<CodeInstruction> list, int selectIndex,
			out int enumeratorIndex)
		{
			for (var i = selectIndex + 1; i < list.Count && i <= selectIndex + 8; i++)
			{
				var instruction = list[i];
				if (instruction.opcode == OpCodes.Nop)
					continue;
				if (instruction.operand is MethodInfo method && method.Name == "GetEnumerator" &&
					method.DeclaringType == typeof(XmlNodeList))
				{
					enumeratorIndex = i;
					return instruction.labels.Count == 0;
				}
				if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
					break;
			}
			enumeratorIndex = -1;
			return false;
		}
	}
}