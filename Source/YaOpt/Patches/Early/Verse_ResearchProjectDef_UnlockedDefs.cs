using System.Collections.Generic;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Fills the private per-project cache from the index on first access. Vanilla's getter checks
	/// the cache first, so returning true leaves its own logic in charge of the (cheap) return.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	[HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.UnlockedDefs), MethodType.Getter)]
	[EarlyPatch]
	internal static class Verse_ResearchProjectDef_UnlockedDefs
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache);
		}

		private static bool Prefix(ResearchProjectDef __instance)
		{
			if (__instance.cachedUnlockedDefs != null)
				return true;

			var index = DefLookupIndex.DefsByProject;
			if (index == null || !index.TryGetValue(__instance, out var defs))
			{
				// Unknown project (not in the database when the index was built): vanilla behaviour.
				return true;
			}

			// A fresh list per project, like vanilla: callers that mutate the cached list must not
			// affect the index.
			__instance.cachedUnlockedDefs = new List<Def>(defs);
			return true;
		}
	}
}