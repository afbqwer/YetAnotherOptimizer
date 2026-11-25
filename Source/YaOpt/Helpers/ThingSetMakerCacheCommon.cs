using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Shared invalidation stamp and parameter comparison for the ThingSetMaker caches.
	/// </summary>
	/// <remarks>
	/// A sample is only a pure function of its parameters and seed within one game tick. The allowed def
	/// list also reads colonist titles, research progress, the anomaly playstyle and the caller's
	/// validator, so the caches are keyed by the current tick. The epoch covers save loads and def
	/// reloads, after which the tick value can repeat.
	/// </remarks>
	internal static class ThingSetMakerCacheCommon
	{
		internal static int Epoch { get; private set; }

		internal static int Tick => Current.Game?.tickManager?.TicksGame ?? 0;

		static ThingSetMakerCacheCommon()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Advance);
		}

		private static void Advance()
		{
			Epoch++;
		}

		internal static bool ParamsEqual(in ThingSetMakerParams a, in ThingSetMakerParams b)
		{
			return NullableEqual(a.techLevel, b.techLevel)
				&& NullableEqual(a.countRange, b.countRange)
				&& ReferenceEquals(a.filter, b.filter)
				&& ReferenceEquals(a.validator, b.validator)
				&& NullableEqual(a.qualityGenerator, b.qualityGenerator)
				&& NullableEqual(a.maxTotalMass, b.maxTotalMass)
				&& NullableEqual(a.maxThingMarketValue, b.maxThingMarketValue)
				&& NullableEqual(a.allowNonStackableDuplicates, b.allowNonStackableDuplicates)
				&& NullableEqual(a.minSingleItemMarketValuePct, b.minSingleItemMarketValuePct)
				&& NullableEqual(a.totalMarketValueRange, b.totalMarketValueRange)
				&& NullableEqual(a.totalNutritionRange, b.totalNutritionRange)
				&& NullableEqual(a.podContentsType, b.podContentsType)
				&& ReferenceEquals(a.makingFaction, b.makingFaction)
				&& ReferenceEquals(a.traderDef, b.traderDef)
				&& NullableEqual(a.tile, b.tile)
				&& ReferenceEquals(a.custom, b.custom);
		}

		private static bool NullableEqual<T>(T? a, T? b) where T : struct
		{
			if (a.HasValue != b.HasValue)
				return false;

			return !a.HasValue || EqualityComparer<T>.Default.Equals(a.GetValueOrDefault(), b.GetValueOrDefault());
		}
	}
}
