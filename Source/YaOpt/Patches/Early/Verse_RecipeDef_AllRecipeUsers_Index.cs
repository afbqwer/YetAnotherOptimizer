using System.Collections.Generic;
using System.Collections.ObjectModel;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Serves <see cref="RecipeDef.AllRecipeUsers" /> from the index. Vanilla has no cache there:
	/// every access allocates an iterator and scans the whole ThingDef database.
	/// </summary>
	/// <remarks>
	/// The early prefix runs before the lazy cache in
	/// <see cref="Patches.Verse_RecipeDef_AllRecipeUsers" />, so when this option is active the
	/// index wins and that patch falls back to vanilla for recipes missing from the index.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	[HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AllRecipeUsers), MethodType.Getter)]
	[EarlyPatch]
	internal static class Verse_RecipeDef_AllRecipeUsers_Index
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache);
		}

		private static bool Prefix(RecipeDef __instance, ref IEnumerable<ThingDef> __result)
		{
			var index = DefLookupIndex.UsersByRecipe;
			if (index == null || !index.TryGetValue(__instance, out ReadOnlyCollection<ThingDef> users))
				return true;

			__result = users;
			return false;
		}
	}
}