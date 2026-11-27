using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Keeps tech hediff installation from aborting pawn generation on degenerate candidate data.
	/// </summary>
	/// <remarks>
	/// <c>GenCollection.RandomElementByWeight</c> returns <c>null</c> after logging when every
	/// candidate weight is zero, and <see cref="PawnTechHediffsGenerator"/> then reads
	/// <c>BaseMarketValue</c> from that null. These helpers keep the vanilla weighted call, random
	/// consumption and market-value lookups; only the null dereferences are replaced.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationFailureGuard"/>
	internal static class TechHediffFallback
	{
		private const int ZeroWeightWarningKey = 0x7A0D011;

		internal static ThingDef PickOrNull(IEnumerable<ThingDef> source,
			Func<ThingDef, float> weightSelector)
		{
			var picked = source.RandomElementByWeight(weightSelector);
			if (picked == null)
			{
				Log.WarningOnce(
					"[YaOpt] Tech hediff generation skipped a candidate pool whose total market value " +
					"is zero. Vanilla would dereference the null pick and abort pawn generation.",
					ZeroWeightWarningKey);
			}

			return picked;
		}

		internal static float MarketValue(ThingDef def)
		{
			return def?.BaseMarketValue ?? 0f;
		}
	}
}