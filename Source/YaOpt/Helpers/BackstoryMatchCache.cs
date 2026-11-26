using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Allocation-free replacement for <see cref="BackstoryCategoryFilter.Matches(BackstoryDef)"/> plus a
	/// per-filter match bitmap.
	/// </summary>
	/// <remarks>
	/// Filling a shuffled backstory slot evaluates one filter against every loaded backstory, and vanilla's
	/// <c>Any(...)</c> predicates allocate a delegate and capture the filter on each evaluation. The result
	/// only depends on the filter and the def data, so it is cached as a bitmap indexed by
	/// <see cref="Def.index"/> and rebuilt when the def count changes or a save loads. A def whose index is
	/// not consistent with the database order is evaluated directly instead of cached.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnBackstoryMatchCache"/>
	/// <seealso cref="Patches.RimWorld_BackstoryCategoryFilter_Matches"/>
	internal static class BackstoryMatchCache
	{
		private sealed class Bitmap
		{
			internal ulong[] Bits;
		}

		private static readonly Dictionary<BackstoryCategoryFilter, Bitmap> Cache =
			new Dictionary<BackstoryCategoryFilter, Bitmap>();

		private static int _defCount = -1;

		static BackstoryMatchCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static void Invalidate()
		{
			Cache.Clear();
			_defCount = -1;
		}

		internal static bool Matches(BackstoryCategoryFilter filter, BackstoryDef backstory)
		{
			var defs = DefDatabase<BackstoryDef>.AllDefsListForReading;
			var count = defs.Count;
			if (count != _defCount)
			{
				Cache.Clear();
				_defCount = count;
			}

			if (!Cache.TryGetValue(filter, out var bitmap))
			{
				bitmap = Build(filter, defs);
				Cache[filter] = bitmap;
			}

			var index = backstory.index;
			if (index < count && defs[index] == backstory)
				return (bitmap.Bits[index >> 6] & (1UL << (index & 63))) != 0;

			return MatchesSlow(filter, backstory);
		}

		private static Bitmap Build(BackstoryCategoryFilter filter, List<BackstoryDef> defs)
		{
			var bits = new ulong[(defs.Count + 63) >> 6];
			for (var i = 0; i < defs.Count; i++)
			{
				var def = defs[i];
				if (!MatchesSlow(filter, def))
					continue;

				var index = def.index;
				if (index < defs.Count && defs[index] == def)
					bits[index >> 6] |= 1UL << (index & 63);
			}

			return new Bitmap { Bits = bits };
		}

		internal static bool MatchesSlow(BackstoryCategoryFilter filter, BackstoryDef backstory)
		{
			var categories = backstory.spawnCategories;
			if (filter.exclude != null && ContainsAny(categories, filter.exclude))
				return false;
			if (HasItems(filter.excludeChildhood) && backstory.slot == BackstorySlot.Childhood
				&& ContainsAny(categories, filter.excludeChildhood))
			{
				return false;
			}
			if (HasItems(filter.excludeAdulthood) && backstory.slot == BackstorySlot.Adulthood
				&& ContainsAny(categories, filter.excludeAdulthood))
			{
				return false;
			}
			if (HasItems(filter.categoriesChildhood) && backstory.slot == BackstorySlot.Childhood
				&& !ContainsAny(categories, filter.categoriesChildhood))
			{
				return false;
			}
			if (HasItems(filter.categoriesAdulthood) && backstory.slot == BackstorySlot.Adulthood
				&& !ContainsAny(categories, filter.categoriesAdulthood))
			{
				return false;
			}
			if (backstory.requiresSpawnCategory)
			{
				if (!HasItems(filter.categories) && !HasItems(filter.categoriesChildhood)
					&& backstory.slot == BackstorySlot.Childhood)
				{
					return false;
				}
				if (!HasItems(filter.categories) && !HasItems(filter.categoriesAdulthood)
					&& backstory.slot == BackstorySlot.Adulthood)
				{
					return false;
				}
			}
			if (filter.categories != null)
				return ContainsAny(categories, filter.categories);

			return true;
		}

		private static bool HasItems(List<string> list)
		{
			return list != null && list.Count > 0;
		}

		private static bool ContainsAny(List<string> source, List<string> values)
		{
			for (var i = 0; i < source.Count; i++)
			{
				if (values.Contains(source[i]))
					return true;
			}

			return false;
		}
	}
}