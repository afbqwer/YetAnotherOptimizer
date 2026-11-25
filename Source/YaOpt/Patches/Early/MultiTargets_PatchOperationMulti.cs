using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
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
			return YaOptGlobal.Settings.OptFastPatchOperation.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var removed = false;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls("SelectNodes"))
				{
					yield return CodeInstruction.Call(
						typeof(XPathReducer),
						nameof(XPathReducer.GetXmlEnumerator));
					continue;
				}
				else if (!removed && instruction.Calls("GetEnumerator"))
				{
					removed = true;
					continue;
				}
				yield return instruction;
			}
		}
	}
}