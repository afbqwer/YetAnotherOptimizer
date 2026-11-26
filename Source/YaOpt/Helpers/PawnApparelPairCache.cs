using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Memoises <see cref="PawnApparelGenerator"/>'s per-definition candidate checks for one generation
	/// parameter set.
	/// </summary>
	/// <remarks>
	/// <c>GenerateStartingApparelFor</c> runs <c>CanUsePair</c> over the whole pair pool with the same
	/// pawn, headgear flag and fixed seed. Everything except the per-pair price only depends on the
	/// <see cref="ThingDef"/>: the headgear test, <c>PawnCanWear</c>, the kind's apparel tags and the seeded
	/// generate chance. Those answers are cached per <see cref="ThingDef"/> until the pawn or one of the
	/// two flags changes, which is exactly when the original results could differ. Prices stay
	/// per pair and are still checked against the current budget.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnApparelPairCache"/>
	/// <seealso cref="Patches.RimWorld_PawnApparelGenerator_CanUsePair"/>
	internal static class PawnApparelPairCache
	{
		[ThreadStatic]
		private static bool _valid;

		[ThreadStatic]
		private static Pawn _pawn;

		[ThreadStatic]
		private static bool _allowHeadgear;

		[ThreadStatic]
		private static int _fixedSeed;

		[ThreadStatic]
		private static Dictionary<ThingDef, bool> _answers;

		static PawnApparelPairCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		private static void Clear()
		{
			_valid = false;
			_pawn = null;
			_answers?.Clear();
		}

		/// <summary>
		/// Returns true when the cached answer can replace <see cref="PawnApparelGenerator.CanUsePair"/>.
		/// </summary>
		internal static bool TryAnswer(ThingStuffPair pair, Pawn pawn, float moneyLeft,
			bool allowHeadgear, int fixedSeed, out bool result)
		{
			result = false;
			if (!_valid || pawn != _pawn || allowHeadgear != _allowHeadgear || fixedSeed != _fixedSeed)
				Reset(pawn, allowHeadgear, fixedSeed);

			if (pair.Price > moneyLeft)
				return true;

			var thing = pair.thing;
			if (_answers.TryGetValue(thing, out result))
				return true;

			result = Evaluate(thing, pawn, allowHeadgear, fixedSeed);
			_answers[thing] = result;
			return true;
		}

		private static void Reset(Pawn pawn, bool allowHeadgear, int fixedSeed)
		{
			if (_answers == null)
				_answers = new Dictionary<ThingDef, bool>();
			else
				_answers.Clear();

			_pawn = pawn;
			_allowHeadgear = allowHeadgear;
			_fixedSeed = fixedSeed;
			_valid = true;
		}

		/// <summary>
		/// Mirrors the vanilla <c>CanUsePair</c> body without the price check, which stays per pair.
		/// </summary>
		private static bool Evaluate(ThingDef thing, Pawn pawn, bool allowHeadgear, int fixedSeed)
		{
			if (!allowHeadgear && PawnApparelGenerator.IsHeadgear(thing))
				return false;

			var apparel = thing.apparel;
			if (!apparel.PawnCanWear(pawn))
				return false;

			var kind = pawn.kindDef;
			var kindTags = kind.apparelTags;
			if (!kindTags.NullOrEmpty())
			{
				var tags = apparel.tags;
				var found = false;
				for (var i = 0; i < kindTags.Count && !found; i++)
				{
					for (var j = 0; j < tags.Count; j++)
					{
						if (kindTags[i] != tags[j])
							continue;

						found = true;
						break;
					}
				}

				if (!found)
					return false;
			}

			var disallowedTags = kind.apparelDisallowTags;
			if (!disallowedTags.NullOrEmpty())
			{
				var tags = apparel.tags;
				for (var i = 0; i < disallowedTags.Count; i++)
				{
					if (tags.Contains(disallowedTags[i]))
						return false;
				}
			}

			if (!kind.ignoreApparelAllowChance && thing.generateAllowChance < 1f
				&& !Rand.ChanceSeeded(thing.generateAllowChance,
					fixedSeed ^ thing.shortHash ^ 0x3D28557))
			{
				return false;
			}

			return true;
		}
	}
}