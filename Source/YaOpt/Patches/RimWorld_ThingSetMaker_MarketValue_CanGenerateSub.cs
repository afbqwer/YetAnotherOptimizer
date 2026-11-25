using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers <c>CanGenerateSub</c> from the first sampling iteration instead of running the whole
	/// sample.
	/// </summary>
	/// <remarks>
	/// The sample only appends to its chosen list, so a non-empty result is decided by the first
	/// iteration. Option tables call this for every option before generating one of them, and again while
	/// the weighted table is enumerated, which makes it the largest repeated cost in reward and trader
	/// generation. Both calls use the same <c>nextSeed</c>, so the answer is memoised per maker instance
	/// for one tick. The sample is drawn under <c>Rand.PushState(nextSeed)</c> so the pool matches what the
	/// following generate call would build, and popping the state keeps the ambient random stream
	/// unchanged.
	/// Disabled automatically when another mod patches the mirrored members; see
	/// <see cref="GenerationOptimizationCompat"/>.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath"/>
	/// <seealso cref="ThingSetMakerCanGenerate"/>
	/// <seealso cref="ThingSetMakerFeasibilityCache"/>
	[HarmonyPatch(typeof(ThingSetMaker_MarketValue), "CanGenerateSub")]
	internal static class RimWorld_ThingSetMaker_MarketValue_CanGenerateSub
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptThingSetMakerCanGenerateFastPath.Enabled == true;
		}

		static bool Prefix(ThingSetMaker_MarketValue __instance, ThingSetMakerParams parms, ref bool __result)
		{
			// Subclasses may override AllowedThingDefs; leave them on the vanilla path.
			if (__instance.GetType() != typeof(ThingSetMaker_MarketValue))
				return true;

			var seed = __instance.nextSeed;
			if (ThingSetMakerFeasibilityCache.TryGet(__instance, in parms, seed, out var cached))
			{
				__result = cached;
				return false;
			}

			var allowed = ThingSetMakerUtility.GetAllowedThingDefs(parms).ToList();
			bool feasible;
			if (allowed.Count == 0
				|| (parms.countRange.HasValue && parms.countRange.Value.max <= 0)
				|| !parms.totalMarketValueRange.HasValue
				|| parms.totalMarketValueRange.Value.max <= 0f)
			{
				feasible = false;
			}
			else if (parms.maxTotalMass.HasValue
				&& parms.maxTotalMass.GetValueOrDefault() != float.MaxValue
				&& !ThingSetMakerUtility.PossibleToWeighNoMoreThan(
					allowed,
					parms.techLevel.GetValueOrDefault(),
					parms.maxTotalMass.Value,
					parms.countRange?.min ?? 1))
			{
				feasible = false;
			}
			else
			{
				Rand.PushState(seed);
				try
				{
					// The value draw happens after the seed is pushed in vanilla, so it must happen here too.
					var totalValue = parms.totalMarketValueRange.Value.RandomInRange;
					feasible = ThingSetMakerCanGenerate.FirstIterationFeasible(
						parms,
						totalValue,
						allowed,
						__instance.GetMinValue,
						__instance.GetMaxValue,
						__instance.GetSingleThingValue);
				}
				finally
				{
					Rand.PopState();
				}
			}

			ThingSetMakerFeasibilityCache.Store(__instance, in parms, seed, feasible);
			__result = feasible;
			return false;
		}
	}
}
