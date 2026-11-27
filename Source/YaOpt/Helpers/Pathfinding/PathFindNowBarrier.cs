using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Avoids the global job barrier in <see cref="PathFinder.FindPathNow"/> unless the synchronous call
	/// actually needs to gather data.
	/// </summary>
	/// <remarks>
	/// <see cref="PathFinder.FindPathNow"/> starts with a full <c>ForceCompleteScheduledJobs()</c> so that
	/// the following <c>GatherData</c> cannot mutate the source arrays while worker A* jobs read them.
	/// But <c>GatherData</c> is a no-op once this tick has already gathered, and any tick that scheduled
	/// async jobs also gathered (so <c>lastGatherTick == TicksGame</c>). In that common case no source is
	/// mutated and the barrier only stalls the main thread behind unrelated pawns. When a gather is still
	/// needed the barrier is kept, so the collection race is unchanged.
	/// </remarks>
	internal static class PathFindNowBarrier
	{
		internal static void MaybeForceComplete(PathFinder pathFinder)
		{
			if (pathFinder.mapData.lastGatherTick != GenTicks.TicksGame)
				pathFinder.ForceCompleteScheduledJobs();
		}
	}
}