using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Constructive replacements for <see cref="PawnApparelGenerator.GenerateStartingApparelFor"/> and
	/// <see cref="PawnWeaponGenerator.TryGenerateWeaponFor"/>.
	/// </summary>
	/// <remarks>
	/// Vanilla apparel generation rebuilds the whole candidate set up to 80 times and only keeps a set
	/// that passes random acceptance checks. This implementation filters once, seeds torso and leg
	/// coverage, then fills the budget with weighted picks, reusing the vanilla helper for specific
	/// requirements and the free warm, toxic and vacuum top-ups. Weapons filter once and draw from a
	/// single prefix-sum instead of evaluating every weight twice. Both paths keep the semantic
	/// invariants (mutual exclusivity, budget, required items, race restrictions, naked rules) but no
	/// longer reproduce the vanilla random sequence.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="PawnGearPools"/>
	internal static class PawnGearRebuild
	{
		private enum ApparelKind
		{
			Any,
			Required,
			FullBody,
			TorsoOnSkin,
			Torso,
			Legs
		}

		private static readonly List<ThingStuffPair> ApparelCandidates = new List<ThingStuffPair>();

		private static readonly List<ThingStuffPair> UsableApparel = new List<ThingStuffPair>();

		[ThreadStatic]
		private static float[] _apparelWeights;

		[ThreadStatic]
		private static float[] _usableWeights;

		[ThreadStatic]
		private static float _usableWeightTotal;

		private static float[] _weaponWeights = new float[0];

		private static readonly Dictionary<List<ThingStuffPair>, Dictionary<ThingDef, List<int>>>
			RequiredIndexes = new Dictionary<List<ThingStuffPair>, Dictionary<ThingDef, List<int>>>();

		private static readonly List<int> NoRequiredIndices = new List<int>();

		private static int _requiredIndexDefCount = -1;

		private static readonly List<ThingStuffPair> WarmTorsoCandidates = new List<ThingStuffPair>();

		private static readonly List<ThingStuffPair> WarmHatCandidates = new List<ThingStuffPair>();

		private static readonly List<ThingStuffPair> ToxicCandidates = new List<ThingStuffPair>();

		private static readonly List<ThingStuffPair> VacuumCandidates = new List<ThingStuffPair>();

		private static readonly HashSet<BodyPartGroupDef> UnprotectedVacuumGroups = new HashSet<BodyPartGroupDef>();

		private static readonly List<BodyPartRecord> UnprotectedVacuumParts = new List<BodyPartRecord>();

		static PawnGearRebuild()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(InvalidateIndexes);
		}

		internal static void InvalidateIndexes()
		{
			RequiredIndexes.Clear();
			_requiredIndexDefCount = -1;
		}

		internal static void RebuildApparel(Pawn pawn, PawnGenerationRequest request)
		{
			var previousPool = PawnGearPools.EnterApparelPool(pawn);
			try
			{
				RebuildApparelCore(pawn, request);
			}
			finally
			{
				PawnGearPools.ExitApparelPool(previousPool);
			}
		}

		internal static void RebuildWeapon(Pawn pawn, PawnGenerationRequest request)
		{
			var previousPool = PawnGearPools.EnterWeaponPool(pawn);
			try
			{
				RebuildWeaponCore(pawn, request);
			}
			finally
			{
				PawnGearPools.ExitWeaponPool(previousPool);
			}
		}

		private static void RebuildApparelCore(Pawn pawn, PawnGenerationRequest request)
		{
			if (!pawn.RaceProps.ToolUser || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsAnomalyEntity)
				return;

			pawn.apparel.DestroyAll();
			pawn.outfits?.forcedHandler?.Reset();

			if (PawnGearRebuildCompat.RequiresTopUpWearValidation)
			{
				if (_topUpWearable == null)
					_topUpWearable = new Dictionary<ThingDef, bool>();
				else
					_topUpWearable.Clear();
			}

			var environment = PawnGearEnvironment.Resolve(pawn, request,
				YaOptGlobal.Settings?.OptPawnGearEnvironment.Enabled == true);
			var money = pawn.kindDef.apparelMoney.RandomInRange;
			var warmth = environment.Warmth;
			var needVacuum = environment.NeedVacuum;
			var allowHeadgear = Rand.Value < pawn.kindDef.apparelAllowHeadgearChance;
			var pollution = environment.Pollution;
			var fixedSeed = Rand.Int;

			var pool = PawnApparelGenerator.allApparelPairs;
			var useIndex = YaOptGlobal.Settings?.OptPawnGenerationIndex.Enabled == true;
			ApparelCandidates.Clear();
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				if (PawnApparelGenerator.CanUsePair(pair, pawn, money, allowHeadgear, fixedSeed)
					&& PawnApparelGenerator.CanUseStuff(pawn, pair))
				{
					ApparelCandidates.Add(pair);
				}
			}

			var set = PawnApparelGenerator.workingSet;
			var nakedOverride = false;
			if (!pawn.IsColonist && Rand.Chance(pawn.kindDef.nakedChance))
			{
				nakedOverride = true;
				set.Reset(pawn);
				PawnApparelGenerator.GenerateSpecificRequiredApparel(pawn, money, onlyGenerateIgnoreNaked: true);
			}
			else if (money < 0.001f)
			{
				PawnApparelGenerator.GenerateWorkingPossibleApparelSetFor(pawn, money, ApparelCandidates);
			}
			else
			{
				FillApparelSet(pawn, money, pool, ApparelCandidates, useIndex);
			}

			if (!pawn.kindDef.apparelIgnoreSeasons || request.ForceAddFreeWarmLayerIfNeeded)
				EnsureTemperatureSafety(pawn, set, environment, nakedOverride);

			if (ModsConfig.BiotechActive && !pawn.kindDef.apparelIgnorePollution && pollution > 0.05f
				&& !set.SatisfiesNeededToxicEnvironmentResistance(pollution))
			{
				AddFreeToxicResistance(pawn, request, set, pollution);
			}

			if (needVacuum)
				AddFreeVacuumResistance(pawn, set);

			GiveToPawn(pawn, set);
			set.Reset(null, null);

			var worn = pawn.apparel.WornApparel;
			for (var i = 0; i < worn.Count; i++)
			{
				var apparel = worn[i];
				PawnApparelGenerator.PostProcessApparel(apparel, pawn);
				var biocodable = apparel.TryGetComp<CompBiocodable>();
				if (biocodable != null && !biocodable.Biocoded && Rand.Chance(request.BiocodeApparelChance))
					biocodable.CodeFor(pawn);
			}
		}

		private static void FillApparelSet(Pawn pawn, float money, List<ThingStuffPair> pool,
			List<ThingStuffPair> candidates, bool useIndex)
		{
			var set = PawnApparelGenerator.workingSet;
			set.Reset(pawn);
			var moneyLeft = PawnApparelGenerator.GenerateSpecificRequiredApparel(pawn, money,
				onlyGenerateIgnoreNaked: false);

			var requiredIndex = useIndex ? EnsureRequiredIndex(pool) : null;
			var required = pawn.kindDef.apparelRequired;
			if (required != null)
			{
				for (var i = 0; i < required.Count; i++)
				{
					var requiredDef = required[i];
					if (!requiredDef.apparel.CorrectAgeForWearing(pawn))
						continue;

					if (requiredIndex == null)
					{
						// Vanilla adds required apparel even when it exceeds the remaining budget.
						// This is the only source that is not pre-filtered by CanUseStuff.
						TryAddWeighted(pawn, pool, set, ref moneyLeft, ApparelKind.Required, requiredDef,
							float.PositiveInfinity, checkStuff: true);
						continue;
					}

					if (!requiredIndex.TryGetValue(requiredDef, out var indices))
						indices = NoRequiredIndices;
					TryAddRequired(pawn, pool, indices, set, ref moneyLeft, requiredDef);
				}
			}

			if (PrefersNoClothing(pawn))
				return;

			UsableApparel.Clear();
			for (var i = 0; i < candidates.Count; i++)
			{
				if (!set.PairOverlapsAnything(candidates[i]))
					UsableApparel.Add(candidates[i]);
			}

			BuildUsableWeights();

			if (!set.Covers(BodyPartGroupDefOf.Torso) && !set.Covers(BodyPartGroupDefOf.Legs)
				&& TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.FullBody, null, moneyLeft))
			{
				PruneUsableApparel(set, moneyLeft);
			}

			if (pawn.gender == Gender.Female
				&& !set.Covers(BodyPartGroupDefOf.Legs)
				&& !set.Covers(BodyPartGroupDefOf.Torso))
			{
				// Torso first, but reserve enough for legs. A torso item that does not cover
				// the legs cannot overlap with them, and a full-body item covers both.
				var cheapestLegs = CheapestKindPrice(pawn, set, ApparelKind.Legs);
				var torsoBudget = moneyLeft;
				if (cheapestLegs < moneyLeft)
				{
					var reserved = moneyLeft - cheapestLegs;
					if (reserved > 0f && HasAffordableKind(pawn, set, ApparelKind.Torso, reserved))
						torsoBudget = reserved;
				}

				TryAddTorso(pawn, set, ref moneyLeft, torsoBudget);

				if (!set.Covers(BodyPartGroupDefOf.Legs))
				{
					TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Legs, null, moneyLeft);
					PruneUsableApparel(set, moneyLeft);
				}
			}
			else
			{
				if (!set.Covers(BodyPartGroupDefOf.Legs))
				{
					TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Legs, null, moneyLeft);
					PruneUsableApparel(set, moneyLeft);
				}

				if (!set.Covers(BodyPartGroupDefOf.Torso))
					TryAddTorso(pawn, set, ref moneyLeft, moneyLeft);
			}

			// The unbounded fill relies on the list being price- and overlap-free at every pick.
			PruneUsableApparel(set, moneyLeft);
			while (TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Any, null, moneyLeft))
				PruneUsableApparel(set, moneyLeft);

			for (var i = 0; i < 2 && set.IsNaked(pawn.gender); i++)
			{
				var kind = !set.Covers(BodyPartGroupDefOf.Legs) ? ApparelKind.Legs : ApparelKind.Torso;
				if (!TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, kind, null, moneyLeft))
					break;

				PruneUsableApparel(set, moneyLeft);
			}
		}

		private static Dictionary<ThingDef, List<int>> EnsureRequiredIndex(List<ThingStuffPair> pool)
		{
			var defCount = DefDatabase<ThingDef>.DefCount;
			if (defCount != _requiredIndexDefCount)
			{
				RequiredIndexes.Clear();
				_requiredIndexDefCount = defCount;
			}

			if (RequiredIndexes.TryGetValue(pool, out var index))
				return index;

			index = new Dictionary<ThingDef, List<int>>();
			for (var i = 0; i < pool.Count; i++)
			{
				var thing = pool[i].thing;
				if (thing?.apparel == null)
					continue;

				if (!index.TryGetValue(thing, out var indices))
				{
					indices = new List<int>();
					index[thing] = indices;
				}

				indices.Add(i);
			}

			RequiredIndexes[pool] = index;
			return index;
		}

		[ThreadStatic]
		private static HashSet<BodyPartGroupDef> _wearableGroups;

		[ThreadStatic]
		private static Dictionary<ThingDef, bool> _topUpWearable;

		/// <summary>
		/// Wears the constructed set without running the vanilla per-item overlap scan.
		/// </summary>
		/// <remarks>
		/// Every pair enters the set through <c>PairOverlapsAnything</c>, which is stricter than
		/// <see cref="ApparelUtility.CanWearTogether"/>, so the removal loop inside the vanilla wear call
		/// can never fire. The body part check is answered from one group set per pawn, and the quadratic
		/// post-validation only runs as a debug assertion. Falls back to vanilla when a foreign patch
		/// touches any of the skipped members.
		/// </remarks>
		private static void GiveToPawn(Pawn pawn, PawnApparelGenerator.PossibleApparelSet set)
		{
			if (!PawnGearRebuildCompat.CanSkipWear)
			{
				set.GiveToPawn(pawn);
				return;
			}

			BuildWearableGroups(pawn);
			var aps = set.ApparelsForReading;
			for (var i = 0; i < aps.Count; i++)
			{
				var pair = aps[i];
				var apparel = (Apparel)ThingMaker.MakeThing(pair.thing, pair.stuff);
				PawnGenerator.PostProcessGeneratedGear(apparel, pawn);
				if (!HasPartsToWear(pair.thing))
				{
					Log.Warning(pawn + " tried to wear " + apparel + " but he has no body parts required to wear it.");
					continue;
				}

				if (!apparel.PawnCanWear(pawn, ignoreGender: true))
				{
					Log.Warning(pawn + " tried to wear " + apparel + " but is not allowed to.");
					continue;
				}

				pawn.apparel.wornApparel.TryAdd(apparel, canMergeWithExistingStacks: false);
			}

			if (DebugViewSettings.logApparelGeneration)
				LogWornApparelConflicts(pawn, aps);
		}

		private static void BuildWearableGroups(Pawn pawn)
		{
			if (_wearableGroups == null)
				_wearableGroups = new HashSet<BodyPartGroupDef>();
			else
				_wearableGroups.Clear();

			foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
			{
				var groups = part.groups;
				for (var i = 0; i < groups.Count; i++)
					_wearableGroups.Add(groups[i]);
			}
		}

		private static bool HasPartsToWear(ThingDef def)
		{
			var groups = def.apparel.bodyPartGroups;
			for (var i = 0; i < groups.Count; i++)
			{
				if (_wearableGroups.Contains(groups[i]))
					return true;
			}

			return false;
		}

		private static void LogWornApparelConflicts(Pawn pawn, List<ThingStuffPair> aps)
		{
			for (var j = 0; j < aps.Count; j++)
			{
				for (var k = 0; k < aps.Count; k++)
				{
					if (j == k || ApparelUtility.CanWearTogether(aps[j].thing, aps[k].thing, pawn.RaceProps.body))
						continue;

					Log.Error(pawn + " generated with apparel that cannot be worn together: " + aps[j] + ", " + aps[k]);
					return;
				}
			}
		}

		private static bool PrefersNoClothing(Pawn pawn)
		{
			if (pawn.Ideo != null && pawn.Ideo.IdeoPrefersNudityForGender(pawn.gender)
				&& (pawn.Faction == null || !pawn.Faction.IsPlayer))
			{
				return true;
			}

			return !pawn.IsColonist && pawn.story?.traits != null && pawn.story.traits.HasTrait(TraitDefOf.Nudist);
		}

		private static void PruneUsableApparel(PawnApparelGenerator.PossibleApparelSet set, float moneyLeft)
		{
			for (var i = UsableApparel.Count - 1; i >= 0; i--)
			{
				var pair = UsableApparel[i];
				if (pair.Price <= moneyLeft && !set.PairOverlapsAnything(pair))
					continue;

				// Swap-remove keeps the parallel weight array aligned in O(1). The element moved
				// into this slot was already tested at its old, higher index and the set did not
				// change since.
				var last = UsableApparel.Count - 1;
				_usableWeightTotal -= _usableWeights[i];
				UsableApparel[i] = UsableApparel[last];
				_usableWeights[i] = _usableWeights[last];
				UsableApparel.RemoveAt(last);
			}
		}

		private static void BuildUsableWeights()
		{
			EnsureUsableWeightCapacity(UsableApparel.Count);
			_usableWeightTotal = 0f;
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var weight = UsableApparel[i].Commonality;
				_usableWeights[i] = weight;
				_usableWeightTotal += weight;
			}
		}

		private static void EnsureUsableWeightCapacity(int count)
		{
			if (_usableWeights != null && _usableWeights.Length >= count)
				return;

			var size = _usableWeights == null ? 16 : _usableWeights.Length * 2;
			while (size < count)
				size *= 2;

			_usableWeights = new float[size];
		}

		private static float CheapestKindPrice(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ApparelKind kind)
		{
			var best = float.PositiveInfinity;
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var pair = UsableApparel[i];
				if (_usableWeights[i] > 0f && pair.Price < best
					&& Matches(pair, pawn, set, kind, null, checkStuff: false))
				{
					best = pair.Price;
				}
			}

			return best;
		}

		private static bool HasAffordableKind(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ApparelKind kind, float priceCap)
		{
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var pair = UsableApparel[i];
				if (pair.Price <= priceCap && _usableWeights[i] > 0f
					&& Matches(pair, pawn, set, kind, null, checkStuff: false))
				{
					return true;
				}
			}

			return false;
		}

		private static void TryAddTorso(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft, float priceCap)
		{
			if (!TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.TorsoOnSkin, null, priceCap))
				TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Torso, null, priceCap);

			PruneUsableApparel(set, moneyLeft);
		}

		private static bool TryAddWeighted(Pawn pawn, List<ThingStuffPair> source,
			PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft, ApparelKind kind,
			ThingDef requiredDef, float priceCap, bool checkStuff = false)
		{
			if (!checkStuff && ReferenceEquals(source, UsableApparel))
				return TryAddWeightedUsable(pawn, set, ref moneyLeft, kind, requiredDef, priceCap);

			EnsureApparelWeights(source.Count);

			// The set is not modified between the weight sum and the draw, so the vanilla second pass
			// recomputes identical values. Caching them halves the per-pick work.
			var total = 0f;
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				var weight = 0f;
				if (pair.Price <= priceCap)
				{
					weight = pair.Commonality;
					if (weight <= 0f || !Matches(pair, pawn, set, kind, requiredDef, checkStuff))
						weight = 0f;
				}

				_apparelWeights[i] = weight;
				total += weight;
			}

			if (total <= 0f)
				return false;

			var roll = Rand.Value * total;
			for (var i = 0; i < source.Count; i++)
			{
				var weight = _apparelWeights[i];
				if (weight <= 0f)
					continue;

				roll -= weight;
				if (roll <= 0f)
				{
					set.Add(source[i]);
					moneyLeft -= source[i].Price;
					return true;
				}
			}

			return false;
		}

		private static bool TryAddRequired(Pawn pawn, List<ThingStuffPair> pool, List<int> indices,
			PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft, ThingDef requiredDef)
		{
			EnsureApparelWeights(indices.Count);
			var total = 0f;
			for (var i = 0; i < indices.Count; i++)
			{
				var poolIndex = indices[i];
				if ((uint)poolIndex >= (uint)pool.Count)
				{
					_apparelWeights[i] = 0f;
					continue;
				}

				var pair = pool[poolIndex];
				var weight = pair.Commonality;
				if (weight <= 0f || !Matches(pair, pawn, set, ApparelKind.Required, requiredDef,
					checkStuff: true))
				{
					weight = 0f;
				}

				_apparelWeights[i] = weight;
				total += weight;
			}

			if (total <= 0f)
				return false;

			var roll = Rand.Value * total;
			for (var i = 0; i < indices.Count; i++)
			{
				var weight = _apparelWeights[i];
				if (weight <= 0f)
					continue;

				roll -= weight;
				if (roll > 0f)
					continue;

				AddToSet(set, ref moneyLeft, pool[indices[i]]);
				return true;
			}

			return false;
		}

		private static void EnsureApparelWeights(int count)
		{
			if (_apparelWeights != null && _apparelWeights.Length >= count)
				return;

			var size = _apparelWeights == null ? 16 : _apparelWeights.Length * 2;
			while (size < count)
				size *= 2;

			_apparelWeights = new float[size];
		}

		private static bool TryAddWeightedUsable(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft, ApparelKind kind,
			ThingDef requiredDef, float priceCap)
		{
			// Every successful pick is followed by a prune, so before an unfiltered pick the list
			// holds only affordable, non-overlapping pairs and the running total is exact.
			if (kind == ApparelKind.Any)
			{
				if (_usableWeightTotal <= 0f)
					return false;

				var roll = Rand.Value * _usableWeightTotal;
				var lastPositive = -1;
				for (var i = 0; i < UsableApparel.Count; i++)
				{
					var weight = _usableWeights[i];
					if (weight <= 0f)
						continue;

					lastPositive = i;
					roll -= weight;
					if (roll <= 0f)
					{
						AddToSet(set, ref moneyLeft, UsableApparel[i]);
						return true;
					}
				}

				// The running total was accumulated in the original order; after swap-removes a
				// float residue can leave the roll positive. Fall back to the last eligible pair.
				if (lastPositive >= 0)
				{
					AddToSet(set, ref moneyLeft, UsableApparel[lastPositive]);
					return true;
				}

				return false;
			}

			EnsureApparelWeights(UsableApparel.Count);
			var total = 0f;
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var weight = _usableWeights[i];
				if (weight <= 0f || UsableApparel[i].Price > priceCap
					|| !Matches(UsableApparel[i], pawn, set, kind, requiredDef, checkStuff: false))
				{
					_apparelWeights[i] = 0f;
					continue;
				}

				_apparelWeights[i] = weight;
				total += weight;
			}

			if (total <= 0f)
				return false;

			var filteredRoll = Rand.Value * total;
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var weight = _apparelWeights[i];
				if (weight <= 0f)
					continue;

				filteredRoll -= weight;
				if (filteredRoll <= 0f)
				{
					AddToSet(set, ref moneyLeft, UsableApparel[i]);
					return true;
				}
			}

			return false;
		}

		private static void AddToSet(PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft,
			ThingStuffPair pair)
		{
			set.Add(pair);
			moneyLeft -= pair.Price;
		}

		private static bool Matches(ThingStuffPair pair, Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ApparelKind kind, ThingDef requiredDef,
			bool checkStuff)
		{
			if (kind == ApparelKind.Required && pair.thing != requiredDef)
				return false;

			if (kind == ApparelKind.Torso || kind == ApparelKind.TorsoOnSkin)
			{
				if (!pair.thing.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso))
					return false;
				if (kind == ApparelKind.TorsoOnSkin
					&& !pair.thing.apparel.layers.Contains(ApparelLayerDefOf.OnSkin))
					return false;
			}
			else if (kind == ApparelKind.FullBody
				&& (!pair.thing.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso)
					|| !CoversGroup(pair, BodyPartGroupDefOf.Legs)))
			{
				return false;
			}
			else if (kind == ApparelKind.Legs && !CoversGroup(pair, BodyPartGroupDefOf.Legs))
			{
				return false;
			}

			return (!checkStuff || PawnApparelGenerator.CanUseStuff(pawn, pair))
				&& !set.PairOverlapsAnything(pair);
		}

		private static bool CoversGroup(ThingStuffPair pair, BodyPartGroupDef group)
		{
			return pair.thing.apparel.bodyPartGroups.Contains(group)
				&& (group != BodyPartGroupDefOf.Legs
					|| !pair.thing.apparel.legsNakedUnlessCoveredBySomethingElse);
		}

		private static void EnsureTemperatureSafety(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, in PawnGearEnvironment environment,
			bool nakedOverride)
		{
			var warmth = environment.Warmth;
			if (warmth == NeededWarmth.Any)
				return;

			float baseMin;
			float baseMax;
			if (environment.HasBaseComfyRange)
			{
				baseMin = environment.BaseComfyMin;
				baseMax = environment.BaseComfyMax;
			}
			else
			{
				baseMin = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin,
					applyPostProcess: true, cacheStaleAfterTicks: 1);
				baseMax = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax,
					applyPostProcess: true, cacheStaleAfterTicks: 1);
			}

			var mapTemperature = environment.MapTemperature;
			ComputeInsulation(set, out var cold, out var heat);
			if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
				&& SatisfiesWarmth(warmth, cold, heat))
			{
				return;
			}

			if (warmth == NeededWarmth.Warm)
			{
				AddFreeWarmth(pawn, set, baseMin, baseMax, mapTemperature);
				ComputeInsulation(set, out cold, out heat);
				if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
					&& SatisfiesWarmth(warmth, cold, heat))
				{
					return;
				}
			}

			if (nakedOverride || PrefersNoClothing(pawn))
				return;

			var needsCold = warmth == NeededWarmth.Warm;
			for (var i = 0; i < 3; i++)
			{
				if (!TryFindTemperatureCandidate(pawn, ApparelCandidates, set, needsCold, baseMin, baseMax,
					cold, heat, mapTemperature, PawnApparelGenerator.freeWarmParkaMaxPrice, out var candidate))
				{
					break;
				}

				AddReplacingConflicts(pawn, set, candidate);
				ComputeInsulation(set, out cold, out heat);
				if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
					&& SatisfiesWarmth(warmth, cold, heat))
				{
					break;
				}
			}
		}

		/// <summary>
		/// Replaces vanilla's full-pool warm top-up with a bounded scan over prefiltered warm apparel.
		/// </summary>
		/// <remarks>
		/// Vanilla walks the whole pool through up to four weighted enumerations, each re-running
		/// <c>GetReplacedInsulationCold</c> per candidate. The candidates are filtered once here, and the
		/// bounded loop then only re-checks the improvement against the current set. Warmth safety and
		/// the parka/hat price caps are preserved; selection is deterministic instead of weighted.
		/// </remarks>
		private static void AddFreeWarmth(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set,
			float baseMin, float baseMax, float mapTemperature)
		{
			BuildWarmCandidates(pawn);
			if (WarmTorsoCandidates.Count == 0 && WarmHatCandidates.Count == 0)
				return;

			ComputeInsulation(set, out var cold, out var heat);
			for (var i = 0; i < 3; i++)
			{
				if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
					&& SatisfiesWarmth(NeededWarmth.Warm, cold, heat))
				{
					break;
				}

				if (!TryFindTemperatureCandidate(pawn, WarmTorsoCandidates, set, needsCold: true,
					baseMin, baseMax, cold, heat, mapTemperature, float.PositiveInfinity, out var candidate))
				{
					break;
				}

				AddReplacingConflicts(pawn, set, candidate);
				ComputeInsulation(set, out cold, out heat);
			}

			if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
				&& SatisfiesWarmth(NeededWarmth.Warm, cold, heat))
			{
				return;
			}

			if (TryFindTemperatureCandidate(pawn, WarmHatCandidates, set, needsCold: true,
				baseMin, baseMax, cold, heat, mapTemperature, float.PositiveInfinity, out var hat))
			{
				AddReplacingConflicts(pawn, set, hat);
			}
		}

		private static bool CanWearForTopUp(ThingDef def, Pawn pawn)
		{
			if (_topUpWearable.TryGetValue(def, out var allowed))
				return allowed;

			allowed = def.apparel.PawnCanWear(pawn, ignoreGender: true);
			_topUpWearable[def] = allowed;
			return allowed;
		}

		private static void BuildWarmCandidates(Pawn pawn)
		{
			WarmTorsoCandidates.Clear();
			WarmHatCandidates.Clear();

			var pool = PawnApparelGenerator.allApparelPairs;
			var parkaCap = PawnApparelGenerator.freeWarmParkaMaxPrice;
			var hatCap = PawnApparelGenerator.freeWarmHatMaxPrice;
			var homeFaction = pawn.HomeFaction?.def;
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				var apparel = pair.thing.apparel;
				if (!apparel.canBeGeneratedToSatisfyWarmth
					|| pair.InsulationCold <= 0f
					|| !apparel.CorrectAgeForWearing(pawn)
					|| (PawnGearRebuildCompat.RequiresTopUpWearValidation
						&& !CanWearForTopUp(pair.thing, pawn))
					|| !CorrectFactionForWarmth(homeFaction, pair.thing))
				{
					continue;
				}

				var groups = apparel.bodyPartGroups;
				if (pair.Price <= parkaCap && groups.Contains(BodyPartGroupDefOf.Torso))
					WarmTorsoCandidates.Add(pair);

				if (pair.Price <= hatCap && pair.InsulationCold >= 7f
					&& (groups.Contains(BodyPartGroupDefOf.FullHead) || groups.Contains(BodyPartGroupDefOf.UpperHead)))
				{
					WarmHatCandidates.Add(pair);
				}
			}
		}

		private static bool CorrectFactionForWarmth(FactionDef faction, ThingDef apparel)
		{
			if (faction == null || apparel.apparel.anyTechLevelCanUseForWarmth)
				return true;
			if ((int)faction.techLevel >= 4 && apparel.techLevel == TechLevel.Neolithic)
				return false;
			if (faction.techLevel == TechLevel.Neolithic && (int)apparel.techLevel >= 4)
				return false;
			return true;
		}

		private static void AddFreeToxicResistance(Pawn pawn, PawnGenerationRequest request,
			PawnApparelGenerator.PossibleApparelSet set, float pollution)
		{
			BuildToxicCandidates(pawn);
			if (ToxicCandidates.Count == 0)
				return;

			for (var i = 0; i < 5 && !set.SatisfiesNeededToxicEnvironmentResistance(pollution); i++)
			{
				if (!TryFindToxicCandidate(pawn, request, set, out var candidate))
					break;

				AddReplacingConflicts(pawn, set, candidate);
			}
		}

		private static void BuildToxicCandidates(Pawn pawn)
		{
			ToxicCandidates.Clear();
			var pool = PawnApparelGenerator.allApparelPairs;
			var cap = PawnApparelGenerator.freeToxicEnvironmentResistanceApparelMaxPrice;
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				var apparel = pair.thing.apparel;
				if (!apparel.canBeGeneratedToSatisfyToxicEnvironmentResistance
					|| pair.ToxicEnvironmentResistance <= 0.25f
					|| pair.Price > cap
					|| !apparel.CorrectAgeForWearing(pawn)
					|| (PawnGearRebuildCompat.RequiresTopUpWearValidation
						&& !CanWearForTopUp(pair.thing, pawn)))
				{
					continue;
				}

				ToxicCandidates.Add(pair);
			}
		}

		private static bool TryFindToxicCandidate(Pawn pawn, PawnGenerationRequest request,
			PawnApparelGenerator.PossibleApparelSet set, out ThingStuffPair best)
		{
			best = default;
			var found = false;
			var bestResistance = 0f;
			var aps = set.ApparelsForReading;
			var body = pawn.RaceProps.body;
			var ignoreInsulation = pawn.kindDef.apparelIgnoreSeasons && !request.ForceAddFreeWarmLayerIfNeeded;
			for (var i = 0; i < ToxicCandidates.Count; i++)
			{
				var pair = ToxicCandidates[i];
				if (IsWornPair(aps, pair))
					continue;

				var rejected = false;
				for (var j = 0; j < aps.Count; j++)
				{
					var existing = aps[j];
					if (ApparelUtility.CanWearTogether(existing.thing, pair.thing, body))
						continue;

					if (existing.ToxicEnvironmentResistance >= 0.25f
						&& pair.ToxicEnvironmentResistance - existing.ToxicEnvironmentResistance <= 0.15f)
					{
						rejected = true;
						break;
					}
				}

				if (rejected)
					continue;

				if (!ignoreInsulation && set.GetReplacedInsulationCold(pair) > pair.InsulationCold)
					continue;

				if (!found || pair.ToxicEnvironmentResistance > bestResistance)
				{
					found = true;
					bestResistance = pair.ToxicEnvironmentResistance;
					best = pair;
				}
			}

			return found;
		}

		private static void AddFreeVacuumResistance(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set)
		{
			var desired = 0.9f - pawn.GetStatValue(StatDefOf.VacuumResistance);
			if (desired <= 0f)
				return;

			BuildVacuumCandidates(pawn);
			if (VacuumCandidates.Count == 0)
				return;

			for (var i = 0; i < 5; i++)
			{
				BuildUnprotectedVacuumParts(pawn, set);
				if (desired >= 0.9f && UnprotectedVacuumParts.Count == 0)
					break;

				BuildUnprotectedVacuumGroups();
				if (!TryFindVacuumCandidate(pawn, set, out var candidate))
					break;

				AddReplacingConflicts(pawn, set, candidate);
			}
		}

		private static void BuildVacuumCandidates(Pawn pawn)
		{
			VacuumCandidates.Clear();
			if (!ModsConfig.OdysseyActive)
				return;

			var pool = PawnApparelGenerator.allApparelPairs;
			var cap = PawnApparelGenerator.freeVacuumResistanceApparelMaxPrice;
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				var apparel = pair.thing.apparel;
				if (!apparel.canBeGeneratedToSatisfyVacuumResistance
					|| pair.VacuumResistance < 0.1f
					|| pair.Price > cap
					|| !apparel.CorrectAgeForWearing(pawn)
					|| (PawnGearRebuildCompat.RequiresTopUpWearValidation
						&& !CanWearForTopUp(pair.thing, pawn)))
				{
					continue;
				}

				VacuumCandidates.Add(pair);
			}
		}

		/// <summary>
		/// Rebuilds the parts vanilla would still need vacuum protection for.
		/// </summary>
		/// <remarks>
		/// Reimplemented instead of calling the vanilla helper because that helper appends to a shared
		/// static list it never clears, so every call grows the list for the rest of the session.
		/// </remarks>
		private static void BuildUnprotectedVacuumParts(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set)
		{
			UnprotectedVacuumParts.Clear();
			var hediffSet = pawn.health.hediffSet;
			UnprotectedVacuumParts.AddRange(
				hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside));

			var aps = set.ApparelsForReading;
			for (var i = UnprotectedVacuumParts.Count - 1; i >= 0; i--)
			{
				var part = UnprotectedVacuumParts[i];
				if (!part.def.canBeVacuumBurnt || !part.def.IsSkinCovered(part, hediffSet))
				{
					UnprotectedVacuumParts.RemoveAt(i);
					continue;
				}

				var covered = false;
				for (var j = 0; j < aps.Count && !covered; j++)
				{
					var worn = aps[j];
					if (worn.VacuumResistance < 0.1f)
						continue;

					var wornGroups = worn.thing.apparel.bodyPartGroups;
					for (var k = 0; k < part.groups.Count; k++)
					{
						if (wornGroups.Contains(part.groups[k]))
						{
							covered = true;
							break;
						}
					}
				}

				if (covered)
					UnprotectedVacuumParts.RemoveAt(i);
			}
		}

		private static void BuildUnprotectedVacuumGroups()
		{
			UnprotectedVacuumGroups.Clear();
			for (var i = 0; i < UnprotectedVacuumParts.Count; i++)
			{
				var groups = UnprotectedVacuumParts[i].groups;
				for (var j = 0; j < groups.Count; j++)
					UnprotectedVacuumGroups.Add(groups[j]);
			}
		}

		private static bool TryFindVacuumCandidate(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, out ThingStuffPair best)
		{
			best = default;
			if (UnprotectedVacuumGroups.Count == 0)
				return false;

			var found = false;
			var bestResistance = 0f;
			var aps = set.ApparelsForReading;
			var body = pawn.RaceProps.body;
			for (var i = 0; i < VacuumCandidates.Count; i++)
			{
				var pair = VacuumCandidates[i];
				if (!CoversAnyGroup(pair.thing.apparel.bodyPartGroups, UnprotectedVacuumGroups)
					|| IsWornPair(aps, pair))
				{
					continue;
				}

				var rejected = false;
				for (var j = 0; j < aps.Count; j++)
				{
					if (!ApparelUtility.CanWearTogether(aps[j].thing, pair.thing, body)
						&& pair.VacuumResistance - aps[j].VacuumResistance <= 0.15f)
					{
						rejected = true;
						break;
					}
				}

				if (rejected)
					continue;

				if (!found || pair.VacuumResistance > bestResistance)
				{
					found = true;
					bestResistance = pair.VacuumResistance;
					best = pair;
				}
			}

			return found;
		}

		private static bool CoversAnyGroup(List<BodyPartGroupDef> groups,
			HashSet<BodyPartGroupDef> target)
		{
			for (var i = 0; i < groups.Count; i++)
			{
				if (target.Contains(groups[i]))
					return true;
			}

			return false;
		}

		private static bool IsWornPair(List<ThingStuffPair> aps, ThingStuffPair pair)
		{
			for (var i = 0; i < aps.Count; i++)
			{
				if (aps[i].thing == pair.thing && aps[i].stuff == pair.stuff)
					return true;
			}

			return false;
		}

		private static void AddReplacingConflicts(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ThingStuffPair candidate)
		{
			var aps = set.ApparelsForReading;
			var body = pawn.RaceProps.body;
			for (var i = aps.Count - 1; i >= 0; i--)
			{
				if (!ApparelUtility.CanWearTogether(aps[i].thing, candidate.thing, body))
					aps.RemoveAt(i);
			}

			aps.Add(candidate);
		}

		private static bool TryFindTemperatureCandidate(Pawn pawn, List<ThingStuffPair> source,
			PawnApparelGenerator.PossibleApparelSet set, bool needsCold,
			float baseMin, float baseMax, float currentCold, float currentHeat, float mapTemperature,
			float maxPrice, out ThingStuffPair best)
		{
			best = default;
			var found = false;
			var bestSafe = false;
			var bestImprovement = float.NegativeInfinity;
			var aps = set.ApparelsForReading;
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				if (pair.Commonality <= 0f
					|| pair.Price > maxPrice
					|| (needsCold ? pair.InsulationCold <= 0f : pair.InsulationHeat <= 0f)
					|| !pair.thing.apparel.CorrectAgeForWearing(pawn))
				{
					continue;
				}

				var alreadyWorn = false;
				var replacedCold = 0f;
				var replacedHeat = 0f;
				for (var j = 0; j < aps.Count; j++)
				{
					var existing = aps[j];
					if (existing.thing == pair.thing && existing.stuff == pair.stuff)
					{
						alreadyWorn = true;
						break;
					}

					if (!ApparelUtility.CanWearTogether(existing.thing, pair.thing, pawn.RaceProps.body))
					{
						replacedCold += existing.InsulationCold;
						replacedHeat += existing.InsulationHeat;
					}
				}

				if (alreadyWorn)
					continue;

				var newCold = currentCold - replacedCold + pair.InsulationCold;
				var newHeat = currentHeat - replacedHeat + pair.InsulationHeat;
				var improvement = needsCold ? newCold - currentCold : newHeat - currentHeat;
				if (improvement <= 0f)
					continue;

				var safe = IsTemperatureSafe(baseMin, baseMax, newCold, newHeat, mapTemperature);
				if (!found
					|| (safe && !bestSafe)
					|| (safe == bestSafe && improvement > bestImprovement))
				{
					found = true;
					bestSafe = safe;
					bestImprovement = improvement;
					best = pair;
				}
			}

			return found;
		}

		private static void ComputeInsulation(PawnApparelGenerator.PossibleApparelSet set,
			out float cold, out float heat)
		{
			cold = 0f;
			heat = 0f;
			var aps = set.ApparelsForReading;
			for (var i = 0; i < aps.Count; i++)
			{
				cold += aps[i].InsulationCold;
				heat += aps[i].InsulationHeat;
			}
		}

		private static bool IsTemperatureSafe(float baseMin, float baseMax, float cold, float heat,
			float mapTemperature)
		{
			return mapTemperature >= baseMin - cold - 10f && mapTemperature <= baseMax + heat + 10f;
		}

		private static bool SatisfiesWarmth(NeededWarmth warmth, float cold, float heat)
		{
			if (warmth == NeededWarmth.Warm)
				return cold >= 52f;
			if (warmth == NeededWarmth.Cool)
				return heat >= -2f;
			return true;
		}

		private static void RebuildWeaponCore(Pawn pawn, PawnGenerationRequest request)
		{
			var working = PawnWeaponGenerator.workingWeapons;
			working.Clear();

			var kind = pawn.kindDef;
			if (kind.weaponTags == null || kind.weaponTags.Count == 0 || !pawn.RaceProps.ToolUser
				|| !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
				|| pawn.WorkTagIsDisabled(WorkTags.Violent))
			{
				return;
			}

			var money = kind.weaponMoney.RandomInRange;
			var shootingDisabled = pawn.WorkTagIsDisabled(WorkTags.Shooting);
			var pool = PawnWeaponGenerator.allWeaponPairs;
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				if (pair.Price > money)
					continue;
				if (!WeaponTagsMatch(pair.thing, kind.weaponTags))
					continue;
				if (kind.weaponStuffOverride != null && pair.stuff != kind.weaponStuffOverride)
					continue;
				if (pair.thing.IsRangedWeapon && shootingDisabled)
					continue;
				if (pair.stuff != null && !pair.stuff.stuffProps.allowedInStuffGeneration)
					continue;
				if (pair.thing.generateAllowChance < 1f
					&& !Rand.ChanceSeeded(pair.thing.generateAllowChance,
						pawn.thingIDNumber ^ pair.thing.shortHash ^ 0x1B3B648))
				{
					continue;
				}

				working.Add(pair);
			}

			if (working.Count == 0)
				return;

			pawn.equipment.DestroyAllEquipment();

			var count = working.Count;
			EnsureWeaponWeights(count);
			var total = 0f;
			for (var i = 0; i < count; i++)
			{
				var weight = PawnWeaponGenerator.GetCommonality(pawn, working[i]);
				if (weight < 0f)
					weight = 0f;
				_weaponWeights[i] = weight;
				total += weight;
			}

			var hasResult = false;
			var result = default(ThingStuffPair);
			if (count == 1 && total > 0f)
			{
				result = working[0];
				hasResult = true;
			}
			else if (total > 0f)
			{
				var roll = Rand.Value * total;
				for (var i = 0; i < count; i++)
				{
					var weight = _weaponWeights[i];
					if (weight <= 0f)
						continue;

					roll -= weight;
					if (roll <= 0f)
					{
						result = working[i];
						hasResult = true;
						break;
					}
				}

				if (!hasResult)
				{
					for (var i = count - 1; i >= 0; i--)
					{
						if (_weaponWeights[i] <= 0f)
							continue;

						result = working[i];
						hasResult = true;
						break;
					}
				}
			}

			if (hasResult)
			{
				var weapon = (ThingWithComps)ThingMaker.MakeThing(result.thing, result.stuff);
				PawnGenerator.PostProcessGeneratedGear(weapon, pawn);
				var equippable = weapon.TryGetComp<CompEquippable>();
				if (equippable != null)
				{
					if (kind.weaponStyleDef != null)
						equippable.parent.StyleDef = kind.weaponStyleDef;
					else if (pawn.Ideo != null)
						equippable.parent.StyleDef = pawn.Ideo.GetStyleFor(weapon.def);
				}

				var biocodeChance = request.BiocodeWeaponChance > 0f
					? request.BiocodeWeaponChance
					: kind.biocodeWeaponChance;
				if (Rand.Value < biocodeChance)
					weapon.TryGetComp<CompBiocodable>()?.CodeFor(pawn);

				pawn.equipment.AddEquipment(weapon);
			}

			working.Clear();
		}

		private static bool WeaponTagsMatch(ThingDef thing, List<string> kindTags)
		{
			var tags = thing.weaponTags;
			if (tags == null)
				return false;

			for (var i = 0; i < kindTags.Count; i++)
			{
				if (tags.Contains(kindTags[i]))
					return true;
			}

			return false;
		}

		private static void EnsureWeaponWeights(int count)
		{
			if (_weaponWeights.Length >= count)
				return;

			var size = Math.Max(count, _weaponWeights.Length * 2);
			_weaponWeights = new float[size];
		}
	}
}