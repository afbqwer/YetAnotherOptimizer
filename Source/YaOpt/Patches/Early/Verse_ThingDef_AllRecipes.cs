using System.Collections.Generic;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Fills the private per-def cache from the index on first access. Vanilla's getter is left
	/// untouched: it checks the cache first and returns it when non-null.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	[HarmonyPatch(typeof(ThingDef), nameof(ThingDef.AllRecipes), MethodType.Getter)]
	[EarlyPatch]
	internal static class Verse_ThingDef_AllRecipes
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache);
		}

		private static bool Prefix(ThingDef __instance)
		{
			if (__instance.allRecipesCached != null)
				return true;

			var index = DefLookupIndex.ByDef;
			if (index == null || !index.TryGetValue(__instance, out var recipes))
			{
				// A def created after the index was built must keep vanilla behaviour.
				return true;
			}

			// A fresh list per def, like vanilla: callers that mutate the cached list must not affect the
			// index or other defs.
			__instance.allRecipesCached = new List<RecipeDef>(recipes);
			return true;
		}
	}
}