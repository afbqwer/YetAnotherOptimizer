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
	/// iteration. Option tables call this for every option before generating one of them, which makes it
	/// the largest repeated cost in reward and trader generation. The sample is drawn under
	/// <c>Rand.PushState(nextSeed)</c> so the pool matches what the following generate call would build,
	/// and popping the state keeps the ambient random stream unchanged.
	/// Disabled automatically when another mod patches the mirrored members; see
	/// <see cref="GenerationOptimizationCompat"/>.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath"/>
	/// <seealso cref="ThingSetMakerCanGenerate"/>
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

			var allowed = ThingSetMakerUtility.GetAllowedThingDefs(parms).ToList();
			if (!allowed.Any())
			{
				__result = false;
				return false;
			}

			if (parms.countRange.HasValue && parms.countRange.Value.max <= 0)
			{
				__result = false;
				return false;
			}

			if (!parms.totalMarketValueRange.HasValue || parms.totalMarketValueRange.Value.max <= 0f)
			{
				__result = false;
				return false;
			}

			if (parms.maxTotalMass.HasValue && parms.maxTotalMass.GetValueOrDefault() != float.MaxValue
				&& !ThingSetMakerUtility.PossibleToWeighNoMoreThan(
					allowed,
					parms.techLevel.GetValueOrDefault(),
					parms.maxTotalMass.Value,
					parms.countRange?.min ?? 1))
			{
				__result = false;
				return false;
			}

			Rand.PushState(__instance.nextSeed);
			try
			{
				// The value draw happens after the seed is pushed in vanilla, so it must happen here too.
				var totalValue = parms.totalMarketValueRange.Value.RandomInRange;
				__result = ThingSetMakerCanGenerate.FirstIterationFeasible(
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

			return false;
		}
	}
}
