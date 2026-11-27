using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Extends the vanilla 5-tick <see cref="StatDefOf.MentalBreakThreshold"/> cache to 120 ticks.
	/// </summary>
	/// <remarks>
	/// The colonist bar reads the mental-breaker thresholds every frame. The stat is already
	/// cacheable, so only the interval supplied by the vanilla 5-tick call sites needs changing.
	/// Trait, gene or hediff changes can take up to 120 ticks (about two seconds) to appear.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptMentalBreakThresholdStatCache"/>
	[HarmonyPatch(typeof(StatWorker))]
	[HarmonyPatch(nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
	internal static class RimWorld_StatWorker_GetValue_MentalBreakThresholdCache
	{
		private const int CacheInterval = 120;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptMentalBreakThresholdStatCache.Enabled == true;
		}

		static void Prefix(StatDef ___stat, Thing thing, ref int cacheStaleAfterTicks)
		{
			if (cacheStaleAfterTicks != 5 || ___stat != StatDefOf.MentalBreakThreshold)
				return;
			if (!(thing is Pawn pawn) || !pawn.Spawned)
				return;

			cacheStaleAfterTicks = CacheInterval;
		}
	}
}
