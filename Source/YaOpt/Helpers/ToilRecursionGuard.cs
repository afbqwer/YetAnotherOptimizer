using Verse;
using Verse.AI;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Bounds synchronous toil advancement so a toil graph that jumps back into an immediately
	/// completing toil cannot recurse until the stack overflows.
	/// </summary>
	/// <remarks>
	/// <see cref="JobDriver.TryActuallyStartNextToil"/> runs the next toil's initAction on the
	/// current stack frame. Instant toils keep cascading from there, and PatherArrival toils
	/// complete synchronously when the pawn is already at the target, so a cycle made only of those
	/// edges recurses without bound. On CoreCLR a stack overflow cannot be caught and kills the
	/// process. This guard caps the advances a driver may make in one tick; the skipped call leaves
	/// wantBeginNextToil set, so the driver resumes on the next tick. A driver that still cannot
	/// advance for <see cref="MaxDeferredTicks"/> consecutive ticks is ended with an error instead
	/// of looping forever.
	/// </remarks>
	public static class ToilRecursionGuard
	{
		internal const int MaxAdvancesPerTick = 128;

		/// <summary>Roughly one second at normal game speed.</summary>
		internal const int MaxDeferredTicks = 60;

		/// <summary>
		/// Per-driver guard state, injected into <see cref="JobDriver"/> by the Prepatcher.
		/// </summary>
		public struct State
		{
			/// <summary>Tick the counters below belong to.</summary>
			public int Tick;

			/// <summary>Advance attempts made in <see cref="Tick"/>.</summary>
			public int Advances;

			/// <summary>Consecutive ticks in which the advance budget was exhausted.</summary>
			public int Deferrals;

			/// <summary>The budget was exhausted in <see cref="Tick"/>.</summary>
			public bool Deferred;

			/// <summary>Set when <see cref="Deferrals"/> reaches <see cref="MaxDeferredTicks"/>.</summary>
			public bool AbortPending;
		}

		internal static bool TryBeginAdvance(JobDriver driver)
		{
			ref var state = ref driver.ToilGuard();

			var tick = GenTicks.TicksGame;
			if (state.Tick != tick)
			{
				// A gap breaks the "stuck for N consecutive ticks" streak.
				if (state.Tick != tick - 1 || !state.Deferred)
				{
					state.Deferrals = 0;
				}

				state.Tick = tick;
				state.Advances = 0;
				state.Deferred = false;

				// Abort at the first advance of a new tick, never in the middle of a cascade.
				if (state.AbortPending)
				{
					state = default;
					Abort(driver);
					return false;
				}
			}

			if (state.Advances >= MaxAdvancesPerTick)
			{
				if (!state.Deferred)
				{
					state.Deferred = true;
					if (++state.Deferrals >= MaxDeferredTicks)
					{
						state.AbortPending = true;
					}
				}
				return false;
			}

			state.Advances++;
			return true;
		}

		private static void Abort(JobDriver driver)
		{
			YaOptMod.Error(
				$"{driver.pawn} toil graph made no progress for {MaxDeferredTicks} consecutive ticks " +
				$"(job={driver.job.ToStringSafe()}, toilIndex={driver.CurToilIndex}, " +
				$"over {MaxAdvancesPerTick} toil advances in one tick). Ending the job to prevent a stack overflow. " +
				"A toil loop that never leaves the tick, usually from a mod, is the likely cause.");
			driver.EndJobWith(JobCondition.Errored);
		}
	}
}