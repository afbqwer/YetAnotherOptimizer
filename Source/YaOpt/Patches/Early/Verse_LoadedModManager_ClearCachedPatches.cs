using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Drops the XPath cache built by <see cref="Verse_LoadedModManager_ApplyPatches"/>.
	/// </summary>
	/// <remarks>
	/// <c>ParseAndProcessXML</c> runs after <c>ApplyPatches</c> and lets other mods (for example
	/// PostInheritanceOperation) apply deferred patch operations to the same document. Keeping the
	/// cache until <c>ClearCachedPatches</c> is what makes the optimized lookup cover those
	/// operations instead of paying for the native XPath engine a second time.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastDefPredicateXPath"/>
	[HarmonyPatch(typeof(LoadedModManager))]
	[HarmonyPatch(nameof(LoadedModManager.ClearCachedPatches))]
	[EarlyPatch]
	internal static class Verse_LoadedModManager_ClearCachedPatches
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastPatchOperation.Enabled ||
				   YaOptGlobal.Settings.OptFastDefPredicateXPath.Enabled;
		}

		static void Postfix()
		{
			XPathReducer.ClearCache();
		}
	}
}
