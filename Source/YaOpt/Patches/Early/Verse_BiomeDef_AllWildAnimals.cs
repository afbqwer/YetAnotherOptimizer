using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Serves <see cref="BiomeDef.AllWildAnimals" /> from the per-biome list. Vanilla allocates an
	/// iterator and re-filters every PawnKindDef on each access.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptBiomeAnimalIndex" />
	[HarmonyPatch(typeof(BiomeDef), nameof(BiomeDef.AllWildAnimals), MethodType.Getter)]
	[HarmonyPriority(Priority.Last)]
	[EarlyPatch]
	internal static class Verse_BiomeDef_AllWildAnimals
	{
		private static bool Prepare()
		{
			return true;
		}

		private static bool Prefix(BiomeDef __instance, ref IEnumerable<PawnKindDef> __result)
		{
			if (!DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptBiomeAnimalIndex))
				return true;

			var index = BiomeAnimalIndex.AllWildAnimalsByBiome;
			if (index == null || !index.TryGetValue(__instance, out var animals))
			{
				// Unknown biome: vanilla behaviour.
				return true;
			}

			__result = animals;
			return false;
		}
	}
}