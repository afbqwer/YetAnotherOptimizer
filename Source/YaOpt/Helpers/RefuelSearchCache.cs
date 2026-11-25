using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reuses refuel fuel searches within one game tick.
	/// </summary>
	/// <remarks>
	/// A refuel work giver first calls <c>CanRefuel</c> (which runs a global closest-fuel search
	/// and, for atomic fueling, a region traversal) and then <c>RefuelJob</c> (which repeats the
	/// same searches). No tick passes between them, so the results are valid for the rest of the
	/// tick. Entries are discarded as soon as the tick number changes.
	/// </remarks>
	internal static class RefuelSearchCache
	{
		private readonly struct Key : IEquatable<Key>
		{
			private readonly Pawn _pawn;

			private readonly Thing _target;

			public Key(Pawn pawn, Thing target)
			{
				_pawn = pawn;
				_target = target;
			}

			public bool Equals(Key other)
			{
				return ReferenceEquals(_pawn, other._pawn) && ReferenceEquals(_target, other._target);
			}

			public override bool Equals(object obj)
			{
				return obj is Key other && Equals(other);
			}

			public override int GetHashCode()
			{
				return unchecked((RuntimeHelpers.GetHashCode(_pawn) * 397) ^
								 RuntimeHelpers.GetHashCode(_target));
			}
		}

		private static readonly Dictionary<Key, Thing> BestFuel = new Dictionary<Key, Thing>();

		private static readonly Dictionary<Key, List<Thing>> AllFuel =
			new Dictionary<Key, List<Thing>>();

		private static int _tick = -1;

		static RefuelSearchCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearAll);
		}

		public static bool TryGetBestFuel(Pawn pawn, Thing target, out Thing result)
		{
			result = null;
			if (!BeginTick())
				return false;
			return BestFuel.TryGetValue(new Key(pawn, target), out result);
		}

		public static void StoreBestFuel(Pawn pawn, Thing target, Thing result)
		{
			if (!BeginTick())
				return;
			BestFuel[new Key(pawn, target)] = result;
		}

		public static bool TryGetAllFuel(Pawn pawn, Thing target, out List<Thing> result)
		{
			result = null;
			if (!BeginTick())
				return false;
			return AllFuel.TryGetValue(new Key(pawn, target), out result);
		}

		public static void StoreAllFuel(Pawn pawn, Thing target, List<Thing> result)
		{
			if (!BeginTick())
				return;
			AllFuel[new Key(pawn, target)] = result;
		}

		private static bool BeginTick()
		{
			var tickManager = Find.TickManager;
			if (tickManager == null)
				return false;
			var tick = tickManager.TicksGame;
			if (tick == _tick)
				return true;
			_tick = tick;
			BestFuel.Clear();
			AllFuel.Clear();
			return true;
		}

		private static void ClearAll()
		{
			_tick = -1;
			BestFuel.Clear();
			AllFuel.Clear();
		}
	}
}