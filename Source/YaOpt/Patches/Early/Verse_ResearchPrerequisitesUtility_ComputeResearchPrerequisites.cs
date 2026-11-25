using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Serves the def -> unlocking-projects map from the index. Vanilla rebuilds it from every
	/// research project on every call, which made repeated research UI lookups quadratic.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	[HarmonyPatch(typeof(ResearchPrerequisitesUtility),
		nameof(ResearchPrerequisitesUtility.ComputeResearchPrerequisites))]
	[EarlyPatch]
	internal static class Verse_ResearchPrerequisitesUtility_ComputeResearchPrerequisites
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache);
		}

		private static bool Prefix(ref Dictionary<Def, List<ResearchProjectDef>> __result)
		{
			var index = DefLookupIndex.PrerequisitesByDef;
			if (index == null)
				return true;

			__result = index;
			return false;
		}
	}
}