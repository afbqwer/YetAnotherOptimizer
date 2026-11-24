using HarmonyLib;
using System.Reflection;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Invalidates world-map path and material caches before and after a mod content reload.
	/// </summary>
	[HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.ReloadContentInt))]
	internal static class Verse_ModContentPack_ReloadContentInt
	{
		private static bool Prepare()
		{
			return WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapTextureCache) ||
				   WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapRegrowth);
		}

		[HarmonyPriority(Priority.First)]
		private static void Prefix()
		{
			ClearCaches();
		}

		private static void Postfix()
		{
			ClearCaches();
		}

		private static void ClearCaches()
		{
			Verse_ContentFinder_TextureGet.ClearCache();
			ReGrowthCore_ReGrowthMod_DefaultWMBPTextureFallback.ClearCache();
			ReGrowthCore_WorldDrawLayer_Beautification_Regenerate.ClearCaches();
		}
	}
}
