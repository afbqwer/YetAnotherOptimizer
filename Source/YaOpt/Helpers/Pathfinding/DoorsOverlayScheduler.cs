using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// By-value replacement for the <see cref="PathGridDoorsBlockedJob"/> class instance that vanilla wraps in
	/// a <c>ManagedJob</c>. Because <c>IJobExtensions.Schedule</c> copies the job into a pooled runner, a
	/// struct carrying the managed lists can be scheduled directly, without a GC handle and without a second
	/// dispose job.
	/// </summary>
	internal struct DoorsOverlayJob : IJob
	{
		public Map map;

		public Pawn pawn;

		public IntVec3 start;

		public IntVec3 dest;

		public TraverseParms traverseParams;

		public PathFinderCostTuning tuning;

		public IReadOnlyList<Thing> doors;

		public IReadOnlyList<IPathFindCostProvider> providers;

		public IReadOnlyList<Pawn> pawns;

		public NativeArray<ushort> providerCost;

		public NativeArray<bool> blocked;

		public void Execute()
		{
			OptimizedDoorsJob.ExecuteCore(map, pawn, start, dest, traverseParams, tuning,
				doors, providers, pawns, providerCost, blocked);
		}
	}

	internal static class DoorsOverlayScheduler
	{
		/// <summary>
		/// Parameters and schedules the door/pawn overlay for one request. Mirrors
		/// <c>ParameterizeDoorBlockedJob</c> but targets the by-value <see cref="DoorsOverlayJob"/>.
		/// </summary>
		internal static JobHandle Schedule(PathFinder pathFinder, PathRequest request,
			ref PathFinder.PathUniqueState state, JobHandle dependsOn)
		{
			return IJobExtensions.Schedule(Create(pathFinder, request, ref state), dependsOn);
		}

		/// <summary>
		/// Parameters and runs the door/pawn overlay synchronously on the calling thread, as the
		/// <c>FindPathNow</c> path does.
		/// </summary>
		internal static void Run(PathFinder pathFinder, PathRequest request, ref PathFinder.PathUniqueState state)
		{
			Create(pathFinder, request, ref state).Execute();
		}

		private static DoorsOverlayJob Create(PathFinder pathFinder, PathRequest request,
			ref PathFinder.PathUniqueState state)
		{
			pathFinder.EnsureDoorsPawnsCached();
			return new DoorsOverlayJob
			{
				map = pathFinder.map,
				pawn = request.pawn,
				start = request.Start,
				dest = request.Target.Cell,
				traverseParams = request.TraverseParms,
				tuning = request.Tuning,
				doors = pathFinder.cachedDoors,
				providers = pathFinder.cachedProviders,
				pawns = pathFinder.cachedPawns,
				providerCost = state.providers,
				blocked = state.blocked
			};
		}
	}
}