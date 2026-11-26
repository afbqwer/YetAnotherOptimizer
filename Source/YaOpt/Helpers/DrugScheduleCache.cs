using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Holds <see cref="Pawn_DrugPolicyTracker.ShouldTryToTakeScheduledNow"/> results for the current
	/// tick.
	/// </summary>
	/// <remarks>
	/// <c>JobGiver_TakeDrugsForDrugPolicy</c> evaluates every policy entry once for its priority and
	/// again when it builds the job, and each evaluation allocates a closure for
	/// <c>List.Find</c>. The result depends on the pawn, the policy entry and the current tick;
	/// entries are keyed on the position resolved by <see cref="DrugPolicyIndexCache"/> and are
	/// invalidated when a drug is ingested or a suspension ends. While paused the tick does not
	/// advance, so the cache could outlive a policy edit; caching is skipped there.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugScheduleCache"/>
	internal static class DrugScheduledEvalCache
	{
		private sealed class Entry
		{
			public DrugPolicy Policy;

			public ThingDef[] Keys;

			public int[] Stamps;

			public bool[] Results;
		}

		private static readonly ConditionalWeakTable<Pawn, Entry> Entries =
			new ConditionalWeakTable<Pawn, Entry>();

		private static Pawn _lastPawn;

		private static Entry _lastEntry;

		static DrugScheduledEvalCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
			UpdateCallbackHelper.RegisterPreTickCallback(PreTick);
		}

		internal static bool TryGet(Pawn pawn, DrugPolicy policy, ThingDef drug, out bool result)
		{
			result = false;
			if (!CanCache(pawn, policy))
				return false;
			if (!DrugPolicyIndexCache.TryGetIndex(policy, drug, out var index))
				return false;

			var entry = GetEntry(pawn);
			EnsureSize(entry, policy);
			if (entry.Stamps[index] != GenTicks.TicksGame || !ReferenceEquals(entry.Keys[index], drug))
				return false;

			result = entry.Results[index];
			return true;
		}

		internal static void Store(Pawn pawn, DrugPolicy policy, ThingDef drug, bool result)
		{
			if (!CanCache(pawn, policy))
				return;
			if (!DrugPolicyIndexCache.TryGetIndex(policy, drug, out var index))
				return;

			var entry = GetEntry(pawn);
			EnsureSize(entry, policy);
			entry.Keys[index] = drug;
			entry.Stamps[index] = GenTicks.TicksGame;
			entry.Results[index] = result;
		}

		/// <summary>
		/// Drops the pawn's results after its drug take records change.
		/// </summary>
		internal static void Invalidate(Pawn pawn)
		{
			if (pawn == null)
				return;

			Entry entry;
			if (ReferenceEquals(_lastPawn, pawn))
			{
				entry = _lastEntry;
			}
			else if (!Entries.TryGetValue(pawn, out entry))
			{
				return;
			}

			if (entry?.Stamps != null)
				Array.Fill(entry.Stamps, -1);
		}

		private static bool CanCache(Pawn pawn, DrugPolicy policy)
		{
			if (pawn == null || policy == null)
				return false;

			var game = Current.Game;
			if (game == null)
				return false;

			var tickManager = game.tickManager;
			return tickManager != null && !tickManager.Paused;
		}

		private static Entry GetEntry(Pawn pawn)
		{
			if (ReferenceEquals(_lastPawn, pawn) && _lastEntry != null)
				return _lastEntry;

			if (!Entries.TryGetValue(pawn, out var entry))
			{
				entry = new Entry();
				Entries.Add(pawn, entry);
			}

			_lastPawn = pawn;
			_lastEntry = entry;
			return entry;
		}

		private static void EnsureSize(Entry entry, DrugPolicy policy)
		{
			var count = policy.Count;
			if (ReferenceEquals(entry.Policy, policy) && entry.Stamps != null && entry.Stamps.Length == count)
				return;

			entry.Policy = policy;
			if (entry.Stamps == null || entry.Stamps.Length != count)
			{
				entry.Keys = new ThingDef[count];
				entry.Stamps = new int[count];
				entry.Results = new bool[count];
			}

			Array.Fill(entry.Stamps, -1);
		}

		private static void PreTick(int tick)
		{
			_lastPawn = null;
			_lastEntry = null;
		}

		internal static void Clear()
		{
			_lastPawn = null;
			_lastEntry = null;
		}
	}
}