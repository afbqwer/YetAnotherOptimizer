using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Resolves <see cref="DrugPolicy"/> entries by drug def through an index instead of the vanilla
	/// linear scan.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>DrugPolicy.this[ThingDef]</c> walks every entry. The scheduled-drug job givers ask for every
	/// policy entry, and <c>ShouldTryToTakeScheduledNow</c> asks again inside
	/// <c>AllowedToTakeScheduledEver</c>, so a policy with a hundred drug defs performs tens of
	/// thousands of reference comparisons per job search.
	/// </para>
	/// <para>
	/// Each policy keeps an array addressed by <see cref="Def.index"/> holding the policy index plus
	/// one. The entry is revalidated against the policy on every hit, so edits made through the drug
	/// policy UI are seen without an explicit invalidation; a mismatch rebuilds the index once and
	/// then falls back to the vanilla scan.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugPolicyCache"/>
	internal static class DrugPolicyIndexCache
	{
		private sealed class Entry
		{
			public int DefCount;

			/// <summary>
			/// Policy index plus one, or 0 when the def is not in the policy.
			/// </summary>
			public int[] ByDefIndex;
		}

		private static readonly Dictionary<DrugPolicy, Entry> Entries = new Dictionary<DrugPolicy, Entry>();

		private static DrugPolicy _lastPolicy;

		private static Entry _lastEntry;

		static DrugPolicyIndexCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		/// <summary>
		/// Drops every policy index and the policy references they keep alive.
		/// </summary>
		internal static void Clear()
		{
			Entries.Clear();
			_lastPolicy = null;
			_lastEntry = null;
		}

		/// <summary>
		/// Returns true when the entry was served from the index. False means the caller must fall back
		/// to the vanilla scan, which also preserves its <see cref="ArgumentException"/> on a missing def.
		/// </summary>
		internal static bool TryGet(DrugPolicy policy, ThingDef drug, out DrugPolicyEntry entry)
		{
			entry = null;
			if (!TryGetIndex(policy, drug, out var policyIndex))
				return false;

			entry = policy[policyIndex];
			return true;
		}

		/// <summary>
		/// Resolves the def's position in the policy. Used by caches keyed on the policy entry position.
		/// </summary>
		internal static bool TryGetIndex(DrugPolicy policy, ThingDef drug, out int policyIndex)
		{
			policyIndex = -1;
			if (policy == null || drug == null)
				return false;

			var defCount = DefDatabase<ThingDef>.DefCount;
			if (defCount <= 0)
				return false;

			// A loop over one policy's entries reaches this method repeatedly with the same policy.
			var reused = ReferenceEquals(_lastPolicy, policy);
			var cache = reused && _lastEntry != null ? _lastEntry : GetOrCreate(policy, defCount);
			if (cache == null || cache.ByDefIndex == null)
				return false;

			if (TryIndex(policy, drug, cache, out policyIndex))
				return true;

			// GetOrCreate already produced a fresh index, so a miss there is a genuinely absent def.
			if (!reused)
				return false;

			// The policy was edited since the index was built (entry added, removed, reordered or
			// replaced). Rebuild once; a miss after that is a genuinely absent def.
			Build(policy, cache, defCount);
			return TryIndex(policy, drug, cache, out policyIndex);
		}

		private static Entry GetOrCreate(DrugPolicy policy, int defCount)
		{
			if (!Entries.TryGetValue(policy, out var cache))
			{
				cache = new Entry();
				Entries[policy] = cache;
			}

			if (cache.ByDefIndex == null || cache.DefCount != defCount)
				Build(policy, cache, defCount);

			_lastPolicy = policy;
			_lastEntry = cache;
			return cache;
		}

		private static bool TryIndex(DrugPolicy policy, ThingDef drug, Entry cache,
			out int policyIndex)
		{
			policyIndex = -1;
			var slots = cache.ByDefIndex;
			var index = drug.index;
			if (slots == null || index >= slots.Length)
				return false;

			var slot = slots[index];
			if (slot == 0)
				return false;

			var candidateIndex = slot - 1;
			if (candidateIndex >= policy.Count)
				return false;

			if (!ReferenceEquals(policy[candidateIndex]?.drug, drug))
				return false;

			policyIndex = candidateIndex;
			return true;
		}

		private static void Build(DrugPolicy policy, Entry cache, int defCount)
		{
			var slots = cache.ByDefIndex;
			if (slots == null || slots.Length != defCount)
			{
				slots = new int[defCount];
				cache.ByDefIndex = slots;
				cache.DefCount = defCount;
			}
			else
			{
				Array.Clear(slots, 0, slots.Length);
			}

			var count = policy.Count;
			for (var i = 0; i < count; i++)
			{
				var def = policy[i]?.drug;
				if (def == null)
					continue;

				var index = def.index;
				if (index >= defCount)
					continue;

				// First occurrence wins, matching the vanilla linear scan.
				if (slots[index] == 0)
					slots[index] = i + 1;
			}
		}
	}

	/// <summary>
	/// Reuses the scheduled-drug job givers' priorities within one tick.
	/// </summary>
	/// <remarks>
	/// <c>ThinkNode_PrioritySorter</c> recomputes every remaining child's priority after each failed
	/// child, so an idle pawn can walk the whole drug policy a dozen times per job search. The value
	/// depends only on the pawn and the current tick, so it is stored per tick and dropped before
	/// the next tick. Keeping only the last pawn also releases the reference every tick.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugPolicyCache"/>
	internal static class DrugJobGiverPriorityCache
	{
		private struct Slot
		{
			public int Tick;

			public Pawn Pawn;

			public float Value;
		}

		private static Slot _takeDrugs;

		private static Slot _moveToInventory;

		static DrugJobGiverPriorityCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
			UpdateCallbackHelper.RegisterPreTickCallback(PreTick);
		}

		internal static bool TryGetTakeDrugs(Pawn pawn, out float value)
		{
			return TryGet(ref _takeDrugs, pawn, out value);
		}

		internal static void StoreTakeDrugs(Pawn pawn, float value)
		{
			Store(ref _takeDrugs, pawn, value);
		}

		internal static bool TryGetMoveToInventory(Pawn pawn, out float value)
		{
			return TryGet(ref _moveToInventory, pawn, out value);
		}

		internal static void StoreMoveToInventory(Pawn pawn, float value)
		{
			Store(ref _moveToInventory, pawn, value);
		}

		internal static void Clear()
		{
			_takeDrugs = default(Slot);
			_moveToInventory = default(Slot);
		}

		private static bool TryGet(ref Slot slot, Pawn pawn, out float value)
		{
			if (slot.Tick == GenTicks.TicksGame && ReferenceEquals(slot.Pawn, pawn))
			{
				value = slot.Value;
				return true;
			}

			value = 0f;
			return false;
		}

		private static void Store(ref Slot slot, Pawn pawn, float value)
		{
			slot.Tick = GenTicks.TicksGame;
			slot.Pawn = pawn;
			slot.Value = value;
		}

		private static void PreTick(int tick)
		{
			// A single pawn reference is enough to keep a discarded map alive between ticks.
			_takeDrugs.Pawn = null;
			_moveToInventory.Pawn = null;
		}
	}
}