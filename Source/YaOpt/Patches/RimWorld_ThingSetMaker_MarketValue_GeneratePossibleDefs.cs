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
	/// chosen one, so the same sample runs many times. The seeded block restores the ambient random state,
	/// so skipping it changes neither the random stream nor the produced items. The sample does read world
	/// state through the allowed def list, so the cache is keyed by tick and invalidated on load; see
	/// <see cref="ThingSetMakerSampleCache"/>.
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
			if (YaOptGlobal.Settings?.OptThingSetMakerSampleCache.Enabled != true)
				return false;

			// The cache replays a call that was skipped: any foreign patch would either be applied twice
			// or lose its side effects. GenerationOptimizationCompat defaults the option off in that case.
			return TargetMethod() != null;
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
