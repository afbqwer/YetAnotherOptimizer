using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Replaces the <c>SelectNodes</c> call with the cached lookup for the patch operations that
	/// snapshot their matches before mutating the tree.
	/// </summary>
	/// <remarks>
	/// Replace, Remove and SetName build <c>SelectNodes(xpath).Cast&lt;XmlNode&gt;().ToArray()</c>
	/// first, because they delete or rename the matched nodes. Returning the same nodes as an array
	/// leaves the existing Cast/ToArray calls untouched; only the XPath evaluation changes, so this
	/// transpiler composes with other patches on the same methods.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastPatchOperation"/>
	[HarmonyPatch]
	[EarlyPatch]
	internal static class MultiTargets_PatchOperationSnapshot
	{
		/// <summary>
		/// <c>ApplyWorker</c> is <c>protected</c> on <see cref="PatchOperation"/> and on every
		/// override of it. Virtual members are deliberately excluded from publicization, so the name
		/// is resolved by reflection in <see cref="AccessTools.Method(Type, string)"/>, which ignores
		/// accessibility.
		/// </summary>
		private const string ApplyWorker = "ApplyWorker";

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(PatchOperationReplace), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationRemove), ApplyWorker);
			yield return AccessTools.Method(typeof(PatchOperationSetName), ApplyWorker);
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastPatchOperation.Enabled ||
				   YaOptGlobal.Settings.OptFastDefPredicateXPath.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = AccessTools.Method(typeof(XPathReducer), nameof(XPathReducer.GetXmlNodes));
			foreach (var instruction in instructions)
			{
				if (instruction.Calls("SelectNodes"))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
				}
				yield return instruction;
			}
		}
	}
}
