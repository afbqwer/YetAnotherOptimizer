using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Verse;
using Verse.AI.Group;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Keeps the cached per-<see cref="PathFinder.MapGridRequest"/> cost grids alive across ticks and
	/// patches only the changed cells instead of discarding every grid whenever any cell delta exists.
	/// </summary>
	/// <remarks>
	/// A grid is a pure function of the per-source arrays plus the key parameters, and every per-cell
	/// source is updated from the same <c>cellDeltas</c> list. So re-running <see cref="PathGridJob.Execute"/>
	/// for the changed cells yields exactly the values a full rebuild would produce, without touching the
	/// rest of the map.
	/// <para/>
	/// A <see cref="PathFinder.MapGridRequest"/> omits three inputs that <c>ParameterizeGridJob</c> reads:
	/// the requester faction, the requester lord and whether the pawn is drafted. Vanilla tolerates this
	/// because any delta recycles every grid, so the grid is re-parameterized from a current request soon
	/// after. A retained template would keep the first request's values indefinitely, so the signature is
	/// stored and re-checked against the first request for the key each gather; a mismatch drops the key.
	/// <para/>
	/// Keys that read per-request data which is not delta-tracked (a customizer grid, an
	/// <see cref="AvoidGrid"/>, or a lord walk grid) are not retained at all. An allowed <see cref="Area"/>
	/// is kept, but its area is liveness-checked on every patch because <see cref="AreaSource.Prune"/>
	/// disposes a removed area's bit array without reporting a source change.
	/// <para/>
	/// Templates are scoped per <see cref="PathFinder"/> (weakly), because a <c>MapGridRequest</c> does not
	/// identify the map and two loaded maps can produce equal keys. Keying globally would let one map's
	/// template patch another map's grid.
	/// </remarks>
	internal static class PathGridIncremental
	{
		/// <summary>Changed-cell x cached-key work above which a full rebuild is cheaper than patching.</summary>
		internal const int MaxPatchWork = 4096;

		/// <summary>
		/// Upper bound on retained keys. Each key holds one full-map <see cref="NativeArray{T}"/>, and unlike
		/// vanilla (which returns every grid to the output pool on each delta tick) a retained key keeps its
		/// array alive, so a churning key space must not grow without limit. Exceeding this forces one vanilla
		/// recycle, which returns the arrays to the pool.
		/// </summary>
		internal const int MaxCachedKeys = 128;

		/// <summary>Set by the ScheduleGridJob postfix's Prepare. When false every patch falls back to vanilla.</summary>
		internal static bool TemplatesReady;

		private struct Template
		{
			public PathGridJob Job;

			public Faction Faction;

			public Lord Lord;

			public bool Drafted;
		}

		private static readonly HashSet<PathFinder> _forceFull = new HashSet<PathFinder>();

		private static ConditionalWeakTable<PathFinder, Dictionary<PathFinder.MapGridRequest, Template>> _templates =
			new ConditionalWeakTable<PathFinder, Dictionary<PathFinder.MapGridRequest, Template>>();

		private static readonly HashSet<PathFinder.MapGridRequest> _validatedKeys = new HashSet<PathFinder.MapGridRequest>();
		private static readonly List<PathFinder.MapGridRequest> _staleKeys = new List<PathFinder.MapGridRequest>();

		internal static void Reset()
		{
			_templates = new ConditionalWeakTable<PathFinder, Dictionary<PathFinder.MapGridRequest, Template>>();
			_forceFull.Clear();
			TemplatesReady = false;
		}

		/// <summary>
		/// Whether a grid may be kept across ticks for this key. This is a <em>retention</em> test, not a
		/// correctness one: a non-retained key (or its template) is dropped on the next delta tick and simply
		/// rebuilt, exactly like vanilla.
		/// </summary>
		/// <remarks>
		/// Only keys that actually recur are worth retaining. <c>targetBuildable</c> is part of the key and is
		/// the build target rect, so a build job produces a fresh key for each target; retaining those would
		/// inflate the cached set, push it into the size/work caps, and evict the genuinely shared base keys.
		/// Avoid-grid, customizer and lord keys are either per-request or read non-delta-tracked data, so they
		/// are dropped the same way. An allowed area is retained and liveness-checked separately in
		/// <see cref="TryPatch"/>.
		/// </remarks>
		internal static bool IsPatchable(PathFinder.MapGridRequest query, PathRequest request)
		{
			if (query.customizer != null)
				return false;
			if (query.avoidGrid != null)
				return false;
			if (query.traverseParams.targetBuildable.Area != 0)
				return false;
			if (request != null && request.RequesterLord != null)
				return false;
			return true;
		}

		internal static void Store(PathFinder pathFinder, in PathFinder.MapGridRequest query, in PathGridJob job,
			PathRequest request)
		{
			var faction = request?.RequesterFaction;
			var lord = request?.RequesterLord;
			var drafted = request != null && request.pawn != null && request.pawn.Drafted;
			_templates.GetOrCreateValue(pathFinder)[query] = new Template
			{
				Job = job,
				Faction = faction,
				Lord = lord,
				Drafted = drafted
			};
		}

		internal static void Forget(PathFinder pathFinder, in PathFinder.MapGridRequest query)
		{
			if (_templates.TryGetValue(pathFinder, out var map))
				map.Remove(query);
		}

		internal static void Clear(PathFinder pathFinder)
		{
			_templates.Remove(pathFinder);
			_forceFull.Remove(pathFinder);
		}

		/// <summary>A faction's cost array was disposed; cached templates may still reference it.</summary>
		internal static void NotifyFactionRemoved(PathFinder pathFinder)
		{
			if (pathFinder != null)
				_forceFull.Add(pathFinder);
		}

		internal static bool ConsumeForceFullRecycle(PathFinder pathFinder)
		{
			return pathFinder != null && _forceFull.Remove(pathFinder);
		}

		/// <summary>
		/// Drops any retained template whose stored request context no longer matches the first request for
		/// that key in this gather. The caller then reschedules the key, so the grid is rebuilt with the
		/// current faction/lord/drafted values, as vanilla would after a recycle.
		/// </summary>
		/// <remarks>
		/// Only the first request per key is compared, matching which request wins when
		/// <c>ScheduleGridJobs</c> builds a missing grid, so minority contexts do not cause rebuild thrash.
		/// </remarks>
		internal static void ValidateContexts(PathFinder pathFinder, IEnumerable<PathRequest> requests)
		{
			if (pathFinder == null || requests == null)
				return;
			if (!_templates.TryGetValue(pathFinder, out var templates) || templates.Count == 0)
				return;

			_validatedKeys.Clear();
			_staleKeys.Clear();

			foreach (var request in requests)
			{
				var key = PathFinder.MapGridRequest.For(request);
				if (!_validatedKeys.Add(key))
					continue;
				if (!templates.TryGetValue(key, out var entry))
					continue;

				var faction = request.RequesterFaction;
				var lord = request.RequesterLord;
				var drafted = request.pawn != null && request.pawn.Drafted;
				if (ReferenceEquals(entry.Faction, faction) && ReferenceEquals(entry.Lord, lord) &&
					entry.Drafted == drafted)
				{
					continue;
				}

				_staleKeys.Add(key);
			}

			if (_staleKeys.Count == 0)
				return;

			var lookup = pathFinder.gridJobLookup;
			for (var i = 0; i < _staleKeys.Count; i++)
			{
				var key = _staleKeys[i];
				templates.Remove(key);
				lookup.Remove(key);
			}
			PruneScheduledGridJobs(pathFinder);
			_staleKeys.Clear();
		}

		/// <summary>
		/// Patches every cached, patchable grid for the changed cells and drops the rest so they are
		/// rebuilt in full on their next request.
		/// </summary>
		/// <returns>
		/// False when the caller must recycle (no patchable path, or the total patch work would exceed a full
		/// rebuild). True when the cache is consistent for this tick (patched, or nothing cached to fix).
		/// </returns>
		internal static bool TryPatch(PathFinder pathFinder, List<IntVec3> cellDeltas)
		{
			if (pathFinder == null)
				return false;

			var lookup = pathFinder.gridJobLookup;
			if (lookup == null || lookup.Count == 0)
				return true;

			if (lookup.Count > MaxCachedKeys)
			{
				PathGridDiagnostics.RecycledKeys();
				return false;
			}

			_templates.TryGetValue(pathFinder, out var templates);
			var templateCount = templates?.Count ?? 0;

			// Work is only done for keys that have a template; lookup.Count also counts non-retained keys
			// (build / avoid grid / customizer / lord) that are never patched and are simply dropped below.
			if ((long)cellDeltas.Count * templateCount > MaxPatchWork)
			{
				PathGridDiagnostics.RecycledWork();
				return false;
			}

			var indices = pathFinder.map.cellIndices;
			List<PathFinder.MapGridRequest> toDrop = null;
			var patchedKeys = 0;

			foreach (var pair in lookup)
			{
				if (templates == null || !templates.TryGetValue(pair.Key, out var template))
				{
					(toDrop ??= new List<PathFinder.MapGridRequest>()).Add(pair.Key);
					continue;
				}

				if (pair.Key.allowedArea != null && !pathFinder.map.areaManager.AllAreas.Contains(pair.Key.allowedArea))
				{
					(toDrop ??= new List<PathFinder.MapGridRequest>()).Add(pair.Key);
					continue;
				}

				for (var i = 0; i < cellDeltas.Count; i++)
				{
					var index = indices.CellToIndex(cellDeltas[i]);
					template.Job.Execute(index);
				}
				patchedKeys++;
			}

			PathGridDiagnostics.Patched(patchedKeys, patchedKeys * cellDeltas.Count);

			if (toDrop != null)
			{
				PathGridDiagnostics.Dropped(toDrop.Count);
				for (var i = 0; i < toDrop.Count; i++)
				{
					var key = toDrop[i];
					lookup.Remove(key);
					templates?.Remove(key);
				}

				// Dropped keys leave finished entries in scheduledGridJobs. Vanilla cleared that list every
				// delta tick, so it never grew; now that recycles are rare we must prune the stale entries so
				// ForceCompleteScheduledJobs stays O(cached keys). Each removed output is recycled exactly once.
				PruneScheduledGridJobs(pathFinder);
			}

			return true;
		}

		/// <summary>Current retained key and template counts, for the debug report.</summary>
		internal static void Snapshot(PathFinder pathFinder, out int lookup, out int templates)
		{
			lookup = pathFinder?.gridJobLookup?.Count ?? 0;
			templates = 0;
			if (pathFinder != null && _templates.TryGetValue(pathFinder, out var map))
				templates = map.Count;
		}

		private static unsafe void PruneScheduledGridJobs(PathFinder pathFinder)
		{
			var jobs = pathFinder.scheduledGridJobs;
			if (jobs.Count == 0)
				return;

			var valid = new HashSet<IntPtr>();
			foreach (var pair in pathFinder.gridJobLookup)
				valid.Add((IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(pair.Value.output.grid));

			for (var i = jobs.Count - 1; i >= 0; i--)
			{
				if (valid.Contains((IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(jobs[i].output.grid)))
					continue;
				pathFinder.gridJobOutputs.Enqueue(jobs[i].output);
				jobs.RemoveAt(i);
			}
		}
	}
}