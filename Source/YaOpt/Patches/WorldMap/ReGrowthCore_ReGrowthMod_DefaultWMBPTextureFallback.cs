using System.Reflection;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches ReGrowth's world-map fallback setting instead of scanning loaded mods for every tile.
	/// </summary>
	/// <remarks>
	/// The getter is called once per tile during world rendering. Harmony's <c>MethodBase __originalMethod</c>
	/// injection would allocate a reflection stub on every call, so the single target uses static fields
	/// instead of a <see cref="Dictionary{TKey, TValue}"/> keyed by method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWorldMapRegrowth"/>
	[HarmonyPatch]
	internal static class ReGrowthCore_ReGrowthMod_DefaultWMBPTextureFallback
	{
		private static bool _cachedValue;

		private static bool _hasCache;

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

		private static bool Prefix(ref bool __result)
		{
			if (!_hasCache)
				return true;

			__result = _cachedValue;
			return false;
		}

		private static void Postfix(bool __result, bool __runOriginal)
		{
			if (!__runOriginal)
				return;

			_cachedValue = __result;
			_hasCache = true;
		}

		internal static void ClearCache()
		{
			_hasCache = false;
		}
	}
}
