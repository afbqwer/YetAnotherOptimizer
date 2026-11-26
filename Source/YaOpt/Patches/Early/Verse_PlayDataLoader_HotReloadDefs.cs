using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	[HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.HotReloadDefs))]
	[EarlyPatch]
	internal static class Verse_PlayDataLoader_HotReloadDefs
	{
		private static bool Prepare()
		{
			return true;
		}

		private static void Prefix()
		{
			DefLookupCacheHelper.InvalidateAll();
		}

		private static void Postfix()
		{
			DefLookupCacheHelper.QueueRebuild("hot reload completed");
		}
	}
}
