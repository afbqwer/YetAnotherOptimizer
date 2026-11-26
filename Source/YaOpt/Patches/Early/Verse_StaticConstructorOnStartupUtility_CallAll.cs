using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Builds the def lookup indexes right before the vanilla static constructor loop, so every
	/// <c>[StaticConstructorOnStartup]</c> constructor and everything after it reads cached data.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	/// <seealso cref="YaOptSettings.OptBiomeAnimalIndex" />
	[HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
	[EarlyPatch]
	internal static class Verse_StaticConstructorOnStartupUtility_CallAll
	{
		private static bool Prepare()
		{
			return true;
		}

		private static void Prefix()
		{
			// CallAll also runs from the deferred loading callback, where the def database is final.
			if (!PlayDataLoader.Loaded)
				return;

			DefLookupCacheHelper.BuildAll("CallAll");
		}

		private static void Postfix()
		{
			if (PlayDataLoader.Loaded)
				DefLookupCacheHelper.QueueRebuild("static constructors completed");
		}
	}
}