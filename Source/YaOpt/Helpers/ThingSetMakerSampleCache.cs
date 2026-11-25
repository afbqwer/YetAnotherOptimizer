using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reuses the deterministic result of <see cref="ThingSetMaker_MarketValue"/>'s sampling.
	/// </summary>
	/// <remarks>
	/// The seeded overload pushes <c>Rand.PushState(seed)</c> around the whole sample, so the result is
	/// a pure function of the merged parameters and the seed, and skipping the call leaves the ambient
	/// random stream untouched. Option tables ask many makers for a result and then generate the chosen
	/// one; the same parameters and seed therefore repeat within a single reward generation.
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

			for (var i = 0; i < memo.Count; i++)
			{
				ref var entry = ref memo.Entries[i];
				if (!entry.Valid || entry.Seed != seed || !ParamsEqual(parms, entry.Params))
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

			for (var i = 0; i < memo.Count; i++)
			{
				ref var existing = ref memo.Entries[i];
				if (existing.Valid && existing.Seed == seed && ParamsEqual(parms, existing.Params))
				{
					Write(ref existing, parms, seed, pairs, total);
					return;
				}
			}

			ref var slot = ref memo.Entries[memo.Next];
			Write(ref slot, parms, seed, pairs, total);
			memo.Next = (memo.Next + 1) % MaxEntries;
			if (memo.Count < MaxEntries)
				memo.Count++;
		}

		private static void Write(ref Entry entry, in ThingSetMakerParams parms, int seed,
			List<ThingStuffPairWithQuality> pairs, float total)
		{
			entry.Params = parms;
			entry.Seed = seed;
			entry.Total = total;
			entry.Pairs = new List<ThingStuffPairWithQuality>(pairs);
			entry.Valid = true;
		}

		private static bool ParamsEqual(in ThingSetMakerParams a, in ThingSetMakerParams b)
		{
			return NullableEqual(a.techLevel, b.techLevel)
				&& NullableEqual(a.countRange, b.countRange)
				&& ReferenceEquals(a.filter, b.filter)
				&& ReferenceEquals(a.validator, b.validator)
				&& NullableEqual(a.qualityGenerator, b.qualityGenerator)
				&& NullableEqual(a.maxTotalMass, b.maxTotalMass)
				&& NullableEqual(a.maxThingMarketValue, b.maxThingMarketValue)
				&& NullableEqual(a.allowNonStackableDuplicates, b.allowNonStackableDuplicates)
				&& NullableEqual(a.minSingleItemMarketValuePct, b.minSingleItemMarketValuePct)
				&& NullableEqual(a.totalMarketValueRange, b.totalMarketValueRange)
				&& NullableEqual(a.totalNutritionRange, b.totalNutritionRange)
				&& NullableEqual(a.podContentsType, b.podContentsType)
				&& ReferenceEquals(a.makingFaction, b.makingFaction)
				&& ReferenceEquals(a.traderDef, b.traderDef)
				&& NullableEqual(a.tile, b.tile)
				&& ReferenceEquals(a.custom, b.custom);
		}

		private static bool NullableEqual<T>(T? a, T? b) where T : struct
		{
			if (a.HasValue != b.HasValue)
				return false;

			return !a.HasValue || EqualityComparer<T>.Default.Equals(a.GetValueOrDefault(), b.GetValueOrDefault());
		}
	}
}
