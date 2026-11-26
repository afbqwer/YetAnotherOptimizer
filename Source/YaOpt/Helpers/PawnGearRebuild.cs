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
			if (!pawn.IsColonist && Rand.Chance(pawn.kindDef.nakedChance))
			{
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

			if ((!pawn.kindDef.apparelIgnoreSeasons || request.ForceAddFreeWarmLayerIfNeeded)
				&& !set.SatisfiesNeededWarmth(warmth, mustBeSafe: true, mapTemperature))
			{
				set.AddFreeWarmthAsNeeded(warmth, mapTemperature, pawn);
			}

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

					TryAddWeighted(pawn, pool, set, ref moneyLeft, ApparelKind.Required, requiredDef);
				}
			}

			if (!AllowsClothing(pawn, money))
				return;

			UsableApparel.Clear();
			for (var i = 0; i < candidates.Count; i++)
			{
				if (!set.PairOverlapsAnything(candidates[i]))
					UsableApparel.Add(candidates[i]);
			}

			if (!set.Covers(BodyPartGroupDefOf.Torso))
			{
				if (!TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.TorsoOnSkin, null)
					&& !set.Covers(BodyPartGroupDefOf.Torso))
				{
					TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Torso, null);
				}

				PruneUsableApparel(set, moneyLeft);
			}

			while (TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, ApparelKind.Any, null))
				PruneUsableApparel(set, moneyLeft);

			for (var i = 0; i < 2 && set.IsNaked(pawn.gender); i++)
			{
				var kind = !set.Covers(BodyPartGroupDefOf.Legs) ? ApparelKind.Legs : ApparelKind.Torso;
				if (!TryAddWeighted(pawn, UsableApparel, set, ref moneyLeft, kind, null))
					break;

				PruneUsableApparel(set, moneyLeft);
			}
		}

		private static bool AllowsClothing(Pawn pawn, float money)
		{
			if (pawn.Ideo != null && pawn.Ideo.IdeoPrefersNudityForGender(pawn.gender)
				&& (pawn.Faction == null || !pawn.Faction.IsPlayer))
			{
				return false;
			}

			if (!pawn.IsColonist && pawn.story?.traits != null && pawn.story.traits.HasTrait(TraitDefOf.Nudist))
				return false;

			return !(Rand.Value < 0.1f) || !(money < 9999999f);
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

		private static bool TryAddWeighted(Pawn pawn, List<ThingStuffPair> source,
			PawnApparelGenerator.PossibleApparelSet set, ref float moneyLeft, ApparelKind kind,
			ThingDef requiredDef)
		{
			var total = 0f;
			for (var i = 0; i < source.Count; i++)
			{
				var pair = source[i];
				if (pair.Price > moneyLeft)
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
				if (pair.Price > moneyLeft)
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