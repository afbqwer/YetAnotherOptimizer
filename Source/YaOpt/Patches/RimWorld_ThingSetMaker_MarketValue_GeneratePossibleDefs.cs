using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reuses the deterministic result of <see cref="ThingSetMaker_MarketValue"/>'s seeded sampling.
	/// </summary>
	/// <remarks>
	/// Option tables query every option with the same merged parameters and seed, then generate the
	/// chosen one, so the same sample runs many times. The result is a pure function of those two inputs
	/// and the seeded block restores the ambient random state, so returning the cached list changes
	/// neither the random stream nor the produced items.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptThingSetMakerSampleCache"/>
	/// <seealso cref="ThingSetMakerSampleCache"/>
	[HarmonyPatch]
	internal static class RimWorld_ThingSetMaker_MarketValue_GeneratePossibleDefs
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(
				typeof(ThingSetMaker_MarketValue),
				nameof(ThingSetMaker_MarketValue.GeneratePossibleDefs),
				new[] { typeof(ThingSetMakerParams), typeof(float).MakeByRefType(), typeof(int) });
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptThingSetMakerSampleCache.Enabled == true;
		}

		static bool Prefix(ThingSetMaker_MarketValue __instance, ThingSetMakerParams parms, int seed,
			ref float totalMarketValue, ref List<ThingStuffPairWithQuality> __result)
		{
			// Subclasses can override AllowedThingDefs with state we cannot reproduce; leave them alone.
			if (__instance.GetType() != typeof(ThingSetMaker_MarketValue) || parms.filter == null)
				return true;

			if (!ThingSetMakerSampleCache.TryGet(__instance, in parms, seed, out var pairs, out var total))
				return true;

			totalMarketValue = total;
			__result = pairs;
			return false;
		}

		static void Postfix(ThingSetMaker_MarketValue __instance, ThingSetMakerParams parms, int seed,
			ref float totalMarketValue, List<ThingStuffPairWithQuality> __result, bool __runOriginal)
		{
			if (!__runOriginal || __result == null)
				return;
			if (__instance.GetType() != typeof(ThingSetMaker_MarketValue) || parms.filter == null)
				return;

			ThingSetMakerSampleCache.Store(__instance, in parms, seed, __result, totalMarketValue);
		}
	}
}
