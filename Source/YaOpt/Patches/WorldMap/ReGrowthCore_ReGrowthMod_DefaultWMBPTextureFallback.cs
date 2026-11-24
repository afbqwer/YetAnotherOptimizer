using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches ReGrowth's world-map fallback setting instead of scanning loaded mods for every tile.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptWorldMapRegrowth"/>
	[HarmonyPatch]
	internal static class ReGrowthCore_ReGrowthMod_DefaultWMBPTextureFallback
	{
		private static readonly Dictionary<MethodBase, bool> _cache = new Dictionary<MethodBase, bool>();

		static ReGrowthCore_ReGrowthMod_DefaultWMBPTextureFallback()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCache);
		}

		private static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("ReGrowthCore.ReGrowthMod");
			return type == null ? null : AccessTools.PropertyGetter(type, "DefaultWMBPTextureFallback");
		}

		private static bool Prepare()
		{
			return WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapRegrowth) &&
				   TargetMethod() != null;
		}

		private static bool Prefix(MethodBase __originalMethod, ref bool __result)
		{
			if (!_cache.TryGetValue(__originalMethod, out var value))
				return true;

			__result = value;
			return false;
		}

		private static void Postfix(MethodBase __originalMethod, bool __result, bool __runOriginal)
		{
			if (__runOriginal)
				_cache[__originalMethod] = __result;
		}

		internal static void ClearCache()
		{
			_cache.Clear();
		}
	}
}
