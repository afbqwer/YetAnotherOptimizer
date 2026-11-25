using System.Runtime.CompilerServices;
using RimWorld;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reuses the first-iteration feasibility answer of <see cref="ThingSetMaker_MarketValue"/>'s
	/// <c>CanGenerateSub</c>.
	/// </summary>
	/// <remarks>
	/// Option tables ask the same maker several times with the same parameters and <c>nextSeed</c>:
	/// once while building the weighted table and again when the table is enumerated to generate.
	/// Without the memo every repeat would rebuild the material pool and rerun the first iteration.
	/// The answer shares the invalidation of the sample cache (tick and epoch), because the mirror
	/// reads the same world state through the allowed def list.
	/// </remarks>
	internal static class ThingSetMakerFeasibilityCache
	{
		private const int MaxEntries = 4;

		private struct Entry
		{
			internal ThingSetMakerParams Params;

			internal int Seed;

			internal int Tick;

			internal int Epoch;

			internal bool Feasible;

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
			out bool feasible)
		{
			feasible = false;

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

				feasible = entry.Feasible;
				return true;
			}

			return false;
		}

		internal static void Store(ThingSetMaker_MarketValue maker, in ThingSetMakerParams parms, int seed,
			bool feasible)
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
					existing.Feasible = feasible;
					return;
				}
			}

			ref var slot = ref memo.Entries[memo.Next];
			slot.Params = parms;
			slot.Seed = seed;
			slot.Tick = tick;
			slot.Epoch = epoch;
			slot.Feasible = feasible;
			slot.Valid = true;
			memo.Next = (memo.Next + 1) % MaxEntries;
			if (memo.Count < MaxEntries)
				memo.Count++;
		}
	}
}
