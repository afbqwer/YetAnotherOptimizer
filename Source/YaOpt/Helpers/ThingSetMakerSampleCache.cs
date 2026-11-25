using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reuses the deterministic result of <see cref="ThingSetMaker_MarketValue"/>'s sampling.
	/// </summary>
	/// <remarks>
	/// The seeded overload pushes <c>Rand.PushState(seed)</c> around the whole sample, so skipping the
	/// call leaves the ambient random stream untouched. Option tables ask many makers for a result and
	/// then generate the chosen one; the same parameters and seed therefore repeat within a single
	/// reward generation.
	/// The result also depends on world state (colonist titles, research progress, anomaly playstyle,
	/// the caller's validator), so entries are keyed by the current tick and by an epoch that advances
	/// on save load. Probed-but-never-chosen makers keep their seed, so without those stamps a later
	/// generation could reuse a sample taken under different world state.
	/// A small per-maker ring holds the recent entries, and callers receive a copy so cached lists are
	/// never mutated.
	/// </remarks>
	internal static class ThingSetMakerSampleCache
	{
		private const int MaxEntries = 4;

		private struct Entry
		{
			internal ThingSetMakerParams Params;

			internal int Seed;

			internal int Tick;

			internal int Epoch;

			internal float Total;

			internal List<ThingStuffPairWithQuality> Pairs;

			internal bool Valid;
		}

		private sealed class Memo
		{
			internal readonly Entry[] Entries = new Entry[MaxEntries];

			internal int Count;

			internal int Next;
		}

		private static readonly ConditionalWeakTable<ThingSetMaker_MarketValue, Memo>.CreateValueCallback CreateMemo =
			_ => new Memo();

		private static readonly ConditionalWeakTable<ThingSetMaker_MarketValue, Memo> Memos =
			new ConditionalWeakTable<ThingSetMaker_MarketValue, Memo>();

		internal static bool TryGet(ThingSetMaker_MarketValue maker, in ThingSetMakerParams parms, int seed,
			out List<ThingStuffPairWithQuality> pairs, out float total)
		{
			pairs = null;
			total = 0f;

			if (!Memos.TryGetValue(maker, out var memo))
				return false;

			var tick = ThingSetMakerCacheCommon.Tick;
			var epoch = ThingSetMakerCacheCommon.Epoch;
			for (var i = 0; i < memo.Count; i++)
			{
				ref var entry = ref memo.Entries[i];
				if (!entry.Valid || entry.Epoch != epoch || entry.Tick != tick
					|| entry.Seed != seed || !ThingSetMakerCacheCommon.ParamsEqual(parms, entry.Params))
					continue;

				pairs = new List<ThingStuffPairWithQuality>(entry.Pairs);
				total = entry.Total;
				return true;
			}

			return false;
		}

		internal static void Store(ThingSetMaker_MarketValue maker, in ThingSetMakerParams parms, int seed,
			List<ThingStuffPairWithQuality> pairs, float total)
		{
			var memo = Memos.GetValue(maker, CreateMemo);
			var tick = ThingSetMakerCacheCommon.Tick;
			var epoch = ThingSetMakerCacheCommon.Epoch;

			for (var i = 0; i < memo.Count; i++)
			{
				ref var existing = ref memo.Entries[i];
				if (existing.Valid && existing.Epoch == epoch && existing.Tick == tick
					&& existing.Seed == seed && ThingSetMakerCacheCommon.ParamsEqual(parms, existing.Params))
				{
					Write(ref existing, parms, seed, tick, epoch, pairs, total);
					return;
				}
			}

			ref var slot = ref memo.Entries[memo.Next];
			Write(ref slot, parms, seed, tick, epoch, pairs, total);
			memo.Next = (memo.Next + 1) % MaxEntries;
			if (memo.Count < MaxEntries)
				memo.Count++;
		}

		private static void Write(ref Entry entry, in ThingSetMakerParams parms, int seed, int tick, int epoch,
			List<ThingStuffPairWithQuality> pairs, float total)
		{
			entry.Params = parms;
			entry.Seed = seed;
			entry.Tick = tick;
			entry.Epoch = epoch;
			entry.Total = total;
			entry.Pairs = new List<ThingStuffPairWithQuality>(pairs);
			entry.Valid = true;
		}
	}
}
