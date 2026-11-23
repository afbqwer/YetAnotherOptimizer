using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Verse;
using Verse.AI;
using YaOpt.Helpers.ThreadSafe;

namespace YaOpt.Helpers.ThreadLocal
{
	/// <summary>
	/// Per-thread copy of the mutable traversal state of the map-wide <see cref="Reachability"/>.
	/// </summary>
	/// <remarks>
	/// 1.6 legacy. On this branch every parallel toggle is locked off in
	/// <c>YaOptSettings.LockedOffOptimizations</c>, so <c>YaOptGlobal.NeedThreadSafe</c> is false and none
	/// of the Reach patches that consume this class are applied.
	///
	/// <see cref="QueueNewOpenRegion"/> deliberately stays a reflection-bound delegate: its callers hold a
	/// <c>Region</c> but no <c>Reachability</c>, and <c>AccessTools.MethodDelegate</c> resolves that by
	/// emitting <c>this = null</c>. It is safe only because the prefix on
	/// <c>Reachability.QueueNewOpenRegion</c> replaces the body and never reads <c>__instance</c>. Do not
	/// copy this pattern — if the parallel path is ever revived, move the enqueue logic into a static
	/// <c>Enqueue(Region)</c> method instead.
	/// </remarks>
	internal class ThreadLocalReachability
	{
		static ThreadLocalReachability()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCache);
			QueueNewOpenRegion =
				AccessTools.MethodDelegate<QueueNewOpenRegionDelegate>(
					AccessTools.Method(typeof(Reachability), nameof(Reachability.QueueNewOpenRegion)), null, false, null);
		}

		public delegate void QueueNewOpenRegionDelegate(Region region);

		// Binds an instance method to a one-parameter delegate, so `this` is emitted as null.
		// Only safe while the prefix replaces the body; see the class remarks.
		public static readonly QueueNewOpenRegionDelegate QueueNewOpenRegion;

		public static ThreadLocal<ThreadLocalReachability> Reachabilities =
			new ThreadLocal<ThreadLocalReachability>(() => new ThreadLocalReachability());

		private static GreedySpinLock _spinLock = new GreedySpinLock();

		public Queue<Region> OpenQueue = new Queue<Region>();

		public List<Region> StartingRegions = new List<Region>();

		public List<Region> DestRegions = new List<Region>();

		public HashSet<int> ReachedRegions = new HashSet<int>();

		public PathGrid PathGrid;

		public RegionGrid RegionGrid;

		private static void ClearCache()
		{
			Reachabilities.Dispose();
			Reachabilities = new ThreadLocal<ThreadLocalReachability>(() => new ThreadLocalReachability());
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ThreadLocalReachability Get()
		{
			return Reachabilities.Value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsRegionAlreadyReached(int regionReachId, int reachId,
			bool isMainThread, HashSet<int> reachedRegions, Region region)
		{
			return isMainThread ? regionReachId == reachId : reachedRegions.Contains(region.id);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void EnterLock(ref bool lockTaken)
		{
			lockTaken = true;
			_spinLock.Enter();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ExitLock(bool lockTaken)
		{
			if (lockTaken)
				_spinLock.Exit();
		}

		public void Clear()
		{
			OpenQueue.Clear();
			StartingRegions.Clear();
			DestRegions.Clear();
			ReachedRegions.Clear();
			PathGrid = null;
			RegionGrid = null;
		}

		public void Setup(Map map, in TraverseParms traverseParams)
		{
			OpenQueue.Clear();
			StartingRegions.Clear();
			DestRegions.Clear();
			ReachedRegions.Clear();
			PathGrid = map.pathing.For(traverseParams).pathGrid;
			RegionGrid = map.regionGrid;
		}
	}
}