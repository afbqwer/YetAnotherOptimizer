using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Drops the indexes when play data is torn down (language change or def hot reload), so a def
	/// instance from the previous load can never be served from them.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	/// <seealso cref="YaOptSettings.OptBiomeAnimalIndex" />
	[HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.ClearAllPlayData))]
	[EarlyPatch]
	internal static class Verse_PlayDataLoader_ClearAllPlayData
	{
		private static bool Prepare()
		{
			return DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache) ||
				   DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptBiomeAnimalIndex);
		}

		private static void Prefix()
		{
			DefLookupIndex.Invalidate();
			BiomeAnimalIndex.Invalidate();
		}
	}
}