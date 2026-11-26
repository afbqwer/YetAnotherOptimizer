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

		private static float[] _weaponWeights = new float[0];

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

			var money = pawn.kindDef.apparelMoney.RandomInRange;
			var warmth = PawnApparelGenerator.ApparelWarmthNeededNow(pawn, request, out var mapTemperature);
			var needVacuum = PawnApparelGenerator.NeedVacuumResistance(pawn, request);
			var allowHeadgear = Rand.Value < pawn.kindDef.apparelAllowHeadgearChance;
			var pollution = PawnApparelGenerator.ApparelToxicEnvironmentToAddress(pawn, request);
			var fixedSeed = Rand.Int;

			var pool = PawnApparelGenerator.allApparelPairs;
			ApparelCandidates.Clear();
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				if (PawnApparelGenerator.CanUsePair(pair, pawn, money, allowHeadgear, fixedSeed))
					ApparelCandidates.Add(pair);
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
				FillApparelSet(pawn, money, pool, ApparelCandidates);
			}

			if (!pawn.kindDef.apparelIgnoreSeasons || request.ForceAddFreeWarmLayerIfNeeded)
				EnsureTemperatureSafety(pawn, set, warmth, mapTemperature, nakedOverride);

			if (ModsConfig.BiotechActive && !pawn.kindDef.apparelIgnorePollution && pollution > 0.05f
				&& !set.SatisfiesNeededToxicEnvironmentResistance(pollution))
			{
				set.AddFreeToxicEnvironmentResistanceAsNeeded(pollution, pair =>
				{
					if (!pair.thing.apparel.CorrectAgeForWearing(pawn))
						return false;
					if (pawn.kindDef.apparelIgnoreSeasons && !request.ForceAddFreeWarmLayerIfNeeded)
						return true;
					return !(set.GetReplacedInsulationCold(pair) > pair.InsulationCold);
				});
			}

			if (needVacuum)
				set.AddFreeVacuumResistanceAsNeeded(pawn);

			set.GiveToPawn(pawn);
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
			List<ThingStuffPair> candidates)
		{
			var set = PawnApparelGenerator.workingSet;
			set.Reset(pawn);
			var moneyLeft = PawnApparelGenerator.GenerateSpecificRequiredApparel(pawn, money,
				onlyGenerateIgnoreNaked: false);

			var required = pawn.kindDef.apparelRequired;
			if (required != null)
			{
				for (var i = 0; i < required.Count; i++)
				{
					var requiredDef = required[i];
					if (!requiredDef.apparel.CorrectAgeForWearing(pawn))
						continue;

					// Vanilla adds required apparel even when it exceeds the remaining budget.
					TryAddWeighted(pawn, pool, set, ref moneyLeft, ApparelKind.Required, requiredDef,
						float.PositiveInfinity);
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
				if (pair.Price > moneyLeft || set.PairOverlapsAnything(pair))
					UsableApparel.RemoveAt(i);
			}
		}

		private static float CheapestKindPrice(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ApparelKind kind)
		{
			var best = float.PositiveInfinity;
			for (var i = 0; i < UsableApparel.Count; i++)
			{
				var pair = UsableApparel[i];
				if (pair.Commonality > 0f && pair.Price < best
					&& Matches(pair, pawn, set, kind, null))
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
				if (pair.Price <= priceCap && pair.Commonality > 0f
					&& Matches(pair, pawn, set, kind, null))
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
			ThingDef requiredDef, float priceCap)
		{
			var total = 0f;
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				if (pair.Price > priceCap)
					continue;

				var weight = pair.Commonality;
				if (weight <= 0f || !Matches(pair, pawn, set, kind, requiredDef))
					continue;

				total += weight;
			}

			if (total <= 0f)
				return false;

			var roll = Rand.Value * total;
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				if (pair.Price > priceCap)
					continue;

				var weight = pair.Commonality;
				if (weight <= 0f || !Matches(pair, pawn, set, kind, requiredDef))
					continue;

				roll -= weight;
				if (roll <= 0f)
				{
					set.Add(pair);
					moneyLeft -= pair.Price;
					return true;
				}
			}

			return false;
		}

		private static bool Matches(ThingStuffPair pair, Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, ApparelKind kind, ThingDef requiredDef)
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

			return PawnApparelGenerator.CanUseStuff(pawn, pair) && !set.PairOverlapsAnything(pair);
		}

		private static bool CoversGroup(ThingStuffPair pair, BodyPartGroupDef group)
		{
			return pair.thing.apparel.bodyPartGroups.Contains(group)
				&& (group != BodyPartGroupDefOf.Legs
					|| !pair.thing.apparel.legsNakedUnlessCoveredBySomethingElse);
		}

		private static void EnsureTemperatureSafety(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, NeededWarmth warmth, float mapTemperature,
			bool nakedOverride)
		{
			if (warmth == NeededWarmth.Any)
				return;

			var baseMin = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin,
				applyPostProcess: true, cacheStaleAfterTicks: 1);
			var baseMax = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax,
				applyPostProcess: true, cacheStaleAfterTicks: 1);

			ComputeInsulation(set, out var cold, out var heat);
			if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
				&& SatisfiesWarmth(warmth, cold, heat))
			{
				return;
			}

			if (warmth == NeededWarmth.Warm)
			{
				set.AddFreeWarmthAsNeeded(warmth, mapTemperature, pawn);
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
				if (!TryFindTemperatureCandidate(pawn, set, needsCold, baseMin, baseMax, cold, heat,
					mapTemperature, out var candidate))
				{
					break;
				}

				var aps = set.ApparelsForReading;
				for (var j = aps.Count - 1; j >= 0; j--)
				{
					if (!ApparelUtility.CanWearTogether(aps[j].thing, candidate.thing, pawn.RaceProps.body))
						aps.RemoveAt(j);
				}

				aps.Add(candidate);
				ComputeInsulation(set, out cold, out heat);
				if (IsTemperatureSafe(baseMin, baseMax, cold, heat, mapTemperature)
					&& SatisfiesWarmth(warmth, cold, heat))
				{
					break;
				}
			}
		}

		private static bool TryFindTemperatureCandidate(Pawn pawn,
			PawnApparelGenerator.PossibleApparelSet set, bool needsCold,
			float baseMin, float baseMax, float currentCold, float currentHeat, float mapTemperature,
			out ThingStuffPair best)
		{
			best = default;
			var found = false;
			var bestSafe = false;
			var bestImprovement = float.NegativeInfinity;
			var aps = set.ApparelsForReading;
			var maxPrice = PawnApparelGenerator.freeWarmParkaMaxPrice;
			for (var i = 0; i < ApparelCandidates.Count; i++)
			{
				var pair = ApparelCandidates[i];
				if (pair.Commonality <= 0f
					|| pair.Price > maxPrice
					|| (needsCold ? pair.InsulationCold <= 0f : pair.InsulationHeat <= 0f)
					|| !PawnApparelGenerator.CanUseStuff(pawn, pair)
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