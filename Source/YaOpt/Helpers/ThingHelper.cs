using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Helper methods for Thing and JobDriver operations used in parallel tick processing.
	/// </summary>
	/// <remarks>
	/// Every member touched here is non-public in vanilla. The project compiles against a
	/// publicized <c>Assembly-CSharp</c>, so they are called directly instead of through
	/// reflection-cached delegates: renames break the build instead of failing at runtime.
	/// </remarks>
	internal static class ThingHelper
	{
		/// <summary>
		/// Checks if a thing should tick at the current interval based on its update rate.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ShouldTickInterval(Thing thing, out int tickDeltaPlusOne)
		{
			tickDeltaPlusOne = thing.tickDelta + 1;
			var minTickIntervalRate = thing.MinTickIntervalRate;
			var maxTickIntervalRate = thing.MaxTickIntervalRate;
			var updateRateTickOffset = thing.UpdateRateTickOffset;
			int num = Mathf.Min(Mathf.Max(thing.UpdateRateTicks, minTickIntervalRate), maxTickIntervalRate);
			return tickDeltaPlusOne >= num || GenTicks.IsTickInterval(updateRateTickOffset, num);
		}

		/// <summary>
		/// Gets the current toil from a JobDriver.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Toil GetCurToil(JobDriver jobDriver)
		{
			return jobDriver.CurToil;
		}

		/// <summary>
		/// Checks if a job should be started from think tree results.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ShouldStartJobFromThinkTree(Pawn_JobTracker jobTracker, ThinkResult thinkResult)
		{
			return jobTracker.ShouldStartJobFromThinkTree(thinkResult);
		}
	}
}
