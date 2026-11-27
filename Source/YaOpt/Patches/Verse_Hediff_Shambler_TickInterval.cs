using HarmonyLib;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Stretches the shambler's periodic target search while it has no target at all.
	/// </summary>
	/// <remarks>
	/// Vanilla reacquires a target every 900-1800 ticks even when the shambler is standing idle
	/// with nothing in mind. When a search finishes without a target, this patch moves the next
	/// one to 9000-12000 ticks. The 180-tick post-engagement grace keeps a shambler that just
	/// lost its target responsive, and active targets or delayed alerts keep the vanilla interval.
	/// Damage, lord alerts and nearby activation still set targets through their own paths.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptShamblerIdleThrottle"/>
	[HarmonyPatch(typeof(Hediff_Shambler))]
	[HarmonyPatch(nameof(Hediff_Shambler.TickInterval))]
	internal static class Verse_Hediff_Shambler_TickInterval
	{
		private const int IDLE_INTERVAL_MIN = 9000;

		private const int IDLE_INTERVAL_MAX = 12000;

		private const int ACTIVE_GRACE_TICKS = 180;

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptShamblerIdleThrottle.Enabled;
		}

		static void Prefix(Hediff_Shambler __instance, out int __state)
		{
			__state = __instance.nextTargetCheckTick;
		}

		static void Postfix(Hediff_Shambler __instance, int __state)
		{
			var nextCheck = __instance.nextTargetCheckTick;
			if (nextCheck == __state)
				return;

			var now = Find.TickManager.TicksGame;
			var delay = nextCheck - now;
			// Only extend intervals shorter than the idle interval. A longer interval set by
			// another mod or by a future vanilla change is left untouched.
			if (delay <= 0 || delay >= IDLE_INTERVAL_MIN)
				return;

			var pawn = __instance.pawn;
			if (!pawn.Spawned || pawn.mutant == null || pawn.mutant.IsPassive || pawn.Drafted ||
				__instance.IsRising)
				return;

			// Active shamblers keep vanilla reacquisition speed.
			if (__instance.alertedTarget != null || pawn.mindState.enemyTarget != null ||
				now - pawn.mindState.lastEngageTargetTick <= ACTIVE_GRACE_TICKS)
				return;

			__instance.nextTargetCheckTick =
				now + Rand.RangeInclusive(IDLE_INTERVAL_MIN, IDLE_INTERVAL_MAX);
		}
	}
}