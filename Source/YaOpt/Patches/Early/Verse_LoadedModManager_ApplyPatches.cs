using HarmonyLib;
using System.Xml;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Creates XPath cache before patch operations for faster lookups. The cache is dropped later by
	/// <see cref="Verse_LoadedModManager_ClearCachedPatches"/> so that the deferred patch operations
	/// applied during <c>ParseAndProcessXML</c> can use it too.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastPatchOperation"/>
	/// <seealso cref="YaOptSettings.OptFastDefPredicateXPath"/>
	[HarmonyPatch(typeof(LoadedModManager))]
	[HarmonyPatch(nameof(LoadedModManager.ApplyPatches))]
	[EarlyPatch]
	internal static class Verse_LoadedModManager_ApplyPatches
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastPatchOperation.Enabled ||
				   YaOptGlobal.Settings.OptFastDefPredicateXPath.Enabled;
		}

		static void Prefix(XmlDocument xmlDoc)
		{
			XPathReducer.CreateCache(xmlDoc);
		}
	}
}