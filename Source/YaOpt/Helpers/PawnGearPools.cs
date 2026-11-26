using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Per-race copies of the vanilla apparel and weapon pair pools, filtered with Humanoid Alien
	/// Races' race restrictions.
	/// </summary>
	/// <remarks>
	/// Vanilla exposes the pools as private static lists. HAR temporarily removes restricted pairs from
	/// the shared list and appends them back afterwards, which walks the whole pool for every pawn and
	/// permanently breaks the price ordering. Swapping the field to a prebuilt per-race list reproduces
	/// the same restriction and leaves the shared list untouched. Without HAR no filtering is needed and
	/// the pools are used as-is.
	/// </remarks>
	/// <seealso cref="PawnGearRebuildCompat"/>
	/// <seealso cref="PawnGearRebuild"/>
	internal static class PawnGearPools
	{
		private static readonly Dictionary<ThingDef, List<ThingStuffPair>> ApparelPools =
			new Dictionary<ThingDef, List<ThingStuffPair>>();

		private static readonly Dictionary<ThingDef, List<ThingStuffPair>> WeaponPools =
			new Dictionary<ThingDef, List<ThingStuffPair>>();

		private static Func<ThingDef, ThingDef, bool> _canWear;

		private static Func<ThingDef, ThingDef, bool> _canEquip;

		private static List<ThingStuffPair> _apparelSource;

		private static List<ThingStuffPair> _weaponSource;

		private static int _defCount = -1;

		[ThreadStatic]
		private static bool _apparelSwapActive;

		[ThreadStatic]
		private static bool _weaponSwapActive;

		static PawnGearPools()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static void Configure(Func<ThingDef, ThingDef, bool> canWear, Func<ThingDef, ThingDef, bool> canEquip)
		{
			_canWear = canWear;
			_canEquip = canEquip;
			Invalidate();
		}

		internal static void Invalidate()
		{
			ApparelPools.Clear();
			WeaponPools.Clear();
			_apparelSource = null;
			_weaponSource = null;
			_defCount = -1;
		}

		/// <summary>
		/// Points <see cref="PawnApparelGenerator.allApparelPairs"/> at the race-filtered pool.
		/// </summary>
		/// <returns>The previous pool when a swap happened, otherwise null.</returns>
		internal static List<ThingStuffPair> EnterApparelPool(Pawn pawn)
		{
			if (_canWear == null || _apparelSwapActive)
				return null;

			EnsureDefCount();
			var source = PawnApparelGenerator.allApparelPairs;
			if (!ReferenceEquals(_apparelSource, source))
			{
				ApparelPools.Clear();
				_apparelSource = source;
			}

			if (!ApparelPools.TryGetValue(pawn.def, out var pool))
			{
				pool = BuildFiltered(source, pawn.def, _canWear);
				ApparelPools[pawn.def] = pool;
			}

			_apparelSwapActive = true;
			PawnApparelGenerator.allApparelPairs = pool;
			return source;
		}

		internal static void ExitApparelPool(List<ThingStuffPair> previous)
		{
			if (previous == null)
				return;

			PawnApparelGenerator.allApparelPairs = previous;
			_apparelSwapActive = false;
		}

		/// <summary>
		/// Points <see cref="PawnWeaponGenerator.allWeaponPairs"/> at the race-filtered pool.
		/// </summary>
		/// <returns>The previous pool when a swap happened, otherwise null.</returns>
		internal static List<ThingStuffPair> EnterWeaponPool(Pawn pawn)
		{
			if (_canEquip == null || _weaponSwapActive)
				return null;

			EnsureDefCount();
			var source = PawnWeaponGenerator.allWeaponPairs;
			if (!ReferenceEquals(_weaponSource, source))
			{
				WeaponPools.Clear();
				_weaponSource = source;
			}

			if (!WeaponPools.TryGetValue(pawn.def, out var pool))
			{
				pool = BuildFiltered(source, pawn.def, _canEquip);
				WeaponPools[pawn.def] = pool;
			}

			_weaponSwapActive = true;
			PawnWeaponGenerator.allWeaponPairs = pool;
			return source;
		}

		internal static void ExitWeaponPool(List<ThingStuffPair> previous)
		{
			if (previous == null)
				return;

			PawnWeaponGenerator.allWeaponPairs = previous;
			_weaponSwapActive = false;
		}

		private static List<ThingStuffPair> BuildFiltered(List<ThingStuffPair> source, ThingDef race,
			Func<ThingDef, ThingDef, bool> allows)
		{
			var result = new List<ThingStuffPair>(source.Count);
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				if (allows(pair.thing, race))
					result.Add(pair);
			}

			return result;
		}

		private static void EnsureDefCount()
		{
			var count = DefDatabase<ThingDef>.DefCount;
			if (count == _defCount)
				return;

			_defCount = count;
			ApparelPools.Clear();
			WeaponPools.Clear();
			_apparelSource = null;
			_weaponSource = null;
		}
	}
}