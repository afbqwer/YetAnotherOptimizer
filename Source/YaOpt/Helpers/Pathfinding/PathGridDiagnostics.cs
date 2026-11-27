using System.Threading;
using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Counters for the incremental grid cache, printed every <see cref="ReportEvery"/> gathers when
	/// debug logging is enabled.
	/// </summary>
	/// <remarks>
	/// Per-method profiling can only show that <c>ScheduleGridJob</c> still runs; it cannot show whether the
	/// same key was reused across ticks, which is the only thing <see cref="PathGridIncremental"/> can save.
	/// These counters compare requests/tick with schedule misses/tick (the difference is cache hits), break
	/// misses down by key category, and record why each recycle happened.
	/// </remarks>
	internal static class PathGridDiagnostics
	{
		private const int ReportEvery = 120;

		private static long _gathers;
		private static long _requests;
		private static long _deltas;
		private static long _scheduled;
		private static long _scheduledBuild;
		private static long _scheduledPlain;
		private static long _scheduledArea;
		private static long _scheduledAvoid;
		private static long _scheduledCustom;
		private static long _scheduledLord;
		private static long _patchedKeys;
		private static long _patchedCells;
		private static long _dropped;
		private static long _recycleWork;
		private static long _recycleKeys;
		private static long _recycleStructural;
		private static long _recycleForce;
		private static int _lookup;
		private static int _templates;

		internal static void Scheduled(in PathFinder.MapGridRequest query, PathRequest request)
		{
			if (!YaOptGlobal.IsDebug)
				return;

			Interlocked.Increment(ref _scheduled);
			if (query.traverseParams.targetBuildable.Area != 0)
				Interlocked.Increment(ref _scheduledBuild);
			else if (query.customizer != null)
				Interlocked.Increment(ref _scheduledCustom);
			else if (query.avoidGrid != null)
				Interlocked.Increment(ref _scheduledAvoid);
			else if (query.allowedArea != null)
				Interlocked.Increment(ref _scheduledArea);
			else if (request != null && request.RequesterLord != null)
				Interlocked.Increment(ref _scheduledLord);
			else
				Interlocked.Increment(ref _scheduledPlain);
		}

		internal static void Patched(int keys, int cells)
		{
			if (!YaOptGlobal.IsDebug)
				return;
			Interlocked.Add(ref _patchedKeys, keys);
			Interlocked.Add(ref _patchedCells, cells);
		}

		internal static void Dropped(int count)
		{
			if (!YaOptGlobal.IsDebug || count <= 0)
				return;
			Interlocked.Add(ref _dropped, count);
		}

		internal static void RecycledWork()
		{
			if (YaOptGlobal.IsDebug)
				Interlocked.Increment(ref _recycleWork);
		}

		internal static void RecycledKeys()
		{
			if (YaOptGlobal.IsDebug)
				Interlocked.Increment(ref _recycleKeys);
		}

		internal static void RecycledStructural()
		{
			if (YaOptGlobal.IsDebug)
				Interlocked.Increment(ref _recycleStructural);
		}

		internal static void RecycledForce()
		{
			if (YaOptGlobal.IsDebug)
				Interlocked.Increment(ref _recycleForce);
		}

		internal static void Gather(int requests, int deltas, int lookup, int templates)
		{
			if (!YaOptGlobal.IsDebug)
				return;

			_gathers++;
			_requests += requests;
			_deltas += deltas;
			_lookup = lookup;
			_templates = templates;
			if (_gathers % ReportEvery == 0)
				Report();
		}

		private static void Report()
		{
			var n = (double)_gathers;
			YaOptMod.Debug(
				$"path-grid: req/tick={_requests / n:F1} sched/tick={_scheduled / n:F1} " +
				$"miss[build={_scheduledBuild / n:F1} plain={_scheduledPlain / n:F1} area={_scheduledArea / n:F1} " +
				$"avoid={_scheduledAvoid / n:F1} custom={_scheduledCustom / n:F1} lord={_scheduledLord / n:F1}] " +
				$"patch/tick={_patchedKeys / n:F1}keys/{_patchedCells / n:F1}cells drop/tick={_dropped / n:F1} " +
				$"lookup={_lookup} templates={_templates} deltas/tick={_deltas / n:F1} " +
				$"recycle[work={_recycleWork} keys={_recycleKeys} struct={_recycleStructural} force={_recycleForce}]");

			_gathers = 0;
			_requests = 0;
			_deltas = 0;
			_scheduled = 0;
			_scheduledBuild = 0;
			_scheduledPlain = 0;
			_scheduledArea = 0;
			_scheduledAvoid = 0;
			_scheduledCustom = 0;
			_scheduledLord = 0;
			_patchedKeys = 0;
			_patchedCells = 0;
			_dropped = 0;
			_recycleWork = 0;
			_recycleKeys = 0;
			_recycleStructural = 0;
			_recycleForce = 0;
		}
	}
}