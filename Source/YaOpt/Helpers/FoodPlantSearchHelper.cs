using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Splits the humanlike food scan into a region-bounded nearest-first plant search plus the
	/// vanilla flat scan over the remaining (non-plant) food sources.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="FoodUtility.BestFoodSourceOnMap"/> sends every nutrition-giving ingestible of the
	/// map through <see cref="FoodUtility.SpawnedFoodSearchInnerScan"/> when the eater can eat
	/// plants. On a normal map that list is dominated by grass, so each of the thousands of plant
	/// candidates pays a full <see cref="FoodUtility.FoodOptimality"/> (and therefore
	/// <see cref="FoodUtility.ThoughtsFromIngesting"/>) before the validator rejects it.
	/// </para>
	/// <para>
	/// For plants the score reduces to <c>300 - dist</c> plus per-def offsets: vanilla's optimality
	/// does not include nutrition, stack count or harvest yield, and <see cref="Plant"/> things
	/// carry no <see cref="CompRottable"/> or <see cref="CompIngredients"/>. So the nearest
	/// reachable plant is the same answer the flat scan produces, except when modded plant defs
	/// differ in <c>optimalityOffsetHumanlikes</c>, trait ingestible modifiers or ideo thoughts -
	/// and only in the tie-breaking of equally scored candidates.
	/// </para>
	/// <para>
	/// Vanilla already uses this strategy for animals (<c>ThingRequestGroup.FoodSource</c> through
	/// <see cref="GenClosest.ClosestThingReachable"/>) and for harvestable plants in the very same
	/// method, so this is not a new search model. The two candidate sets are complementary on
	/// vanilla's own predicate: <c>FoodSourceNotPlantOrTree</c> drops every def whose
	/// <see cref="FoodTypeFlags"/> has no bit outside <c>Plant</c>/<c>Tree</c>, and
	/// <c>ThingRequestGroup.Plant</c> covers the defs that predicate drops, since every plant def
	/// inherits <c>PlantBase</c> (<c>foodType = Plant</c>, <c>category = Plant</c>).
	/// </para>
	/// <para>
	/// Both foreign <see cref="FoodUtility.FoodOptimality"/> postfixes in the reference mod list are
	/// no-ops for plants: Common Sense only touches <see cref="CompRottable"/> items, and Animal
	/// Controls returns early for any eater that has a mood need.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFoodPlantSearchBfs"/>
	/// <seealso cref="Patches.RimWorld_FoodUtility_BestFoodSourceOnMap_PlantBfs"/>
	internal static class FoodPlantSearchHelper
	{
		/// <summary>
		/// Region budget for the plant search, matching the player-animal budget in
		/// <c>FoodUtility.GetMaxRegionsToScan</c>. Exceeding it falls back to the vanilla scan, so a
		/// pawn that is walled off from every plant can never end up with "no food found".
		/// </summary>
		private const int SearchRegionsMax = 100;

		/// <summary>
		/// Drop-in replacement for <see cref="FoodUtility.SpawnedFoodSearchInnerScan"/> used by
		/// <see cref="FoodUtility.BestFoodSourceOnMap"/>.
		/// </summary>
		internal static Thing Search(Pawn eater, IntVec3 root, List<Thing> searchSet, PathEndMode peMode,
			TraverseParms traverseParams, float maxDistance, Predicate<Thing> validator, bool allowPlant,
			bool forceScanWholeMap)
		{
			if (searchSet == null)
				return null;

			Pawn getter = traverseParams.pawn ?? eater;
			Map map = getter?.Map;
			if (!allowPlant || forceScanWholeMap || map == null ||
				(eater.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.Tree)) == 0)
				return FoodUtility.SpawnedFoodSearchInnerScan(eater, root, searchSet, peMode, traverseParams, maxDistance, validator);

			Thing bestPlant = ClosestPlant(map, root, peMode, traverseParams, maxDistance, validator);
			if (bestPlant == null)
				return FoodUtility.SpawnedFoodSearchInnerScan(eater, root, searchSet, peMode, traverseParams, maxDistance, validator);

			List<Thing> nonPlant = map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree));
			Thing bestOther = FoodUtility.SpawnedFoodSearchInnerScan(eater, root, nonPlant, peMode, traverseParams, maxDistance, validator);
			if (bestOther == null)
				return bestPlant;

			// The two winners still have to compete on the real score, otherwise a pawn standing on
			// grass would ignore a meal that vanilla would have picked.
			float plantScore = FoodUtility.FoodOptimality(eater, bestPlant, FoodUtility.GetFinalIngestibleDef(bestPlant),
				(root - bestPlant.Position).LengthManhattan);
			float otherScore = FoodUtility.FoodOptimality(eater, bestOther, FoodUtility.GetFinalIngestibleDef(bestOther),
				(root - bestOther.Position).LengthManhattan);

			// Equal scores keep the flat-scan result; vanilla's "last candidate wins" tie-break is
			// list-order dependent and cannot be reproduced here.
			return plantScore > otherScore ? bestPlant : bestOther;
		}

		private static Thing ClosestPlant(Map map, IntVec3 root, PathEndMode peMode, TraverseParms traverseParams,
			float maxDistance, Predicate<Thing> validator)
		{
			// The vanilla validator dereferences def.ingestible without a null check, and the Plant
			// group also holds trees and bushes that carry no ingestible at all.
			Predicate<Thing> plantValidator = t => t.def.IsNutritionGivingIngestible && (validator == null || validator(t));

			return GenClosest.ClosestThingReachable(root, map, ThingRequest.ForGroup(ThingRequestGroup.Plant), peMode,
				traverseParams, maxDistance, plantValidator, null, 0, SearchRegionsMax, false,
				RegionType.Set_Passable, false);
		}
	}
}
