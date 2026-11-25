using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;
using UnlockedHeader = RimWorld.ResearchPrerequisitesUtility.UnlockedHeader;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Serves the grouped "Unlocks" panel entries from the precomputed index.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	[HarmonyPatch(typeof(ResearchPrerequisitesUtility),
		nameof(ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites))]
	[EarlyPatch]
	internal static class Verse_ResearchPrerequisitesUtility_UnlockedDefsGroupedByPrerequisites
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache);
		}

		private static bool Prefix(ResearchProjectDef rd,
			ref List<Pair<UnlockedHeader, List<Def>>> __result)
		{
			var index = DefLookupIndex.UnlockedGroupsByProject;
			if (index == null || !index.TryGetValue(rd, out var groups))
			{
				// Unknown project: vanilla recomputes it (and falls back consistently through the
				// def-level patch if that project is not part of the index).
				return true;
			}

			__result = groups;
			return false;
		}
	}
}