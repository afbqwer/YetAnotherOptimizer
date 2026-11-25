using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Answers "would this sample produce at least one item" from the first iteration only.
	/// </summary>
	/// <remarks>
	/// Mirrors the first pass of
	/// <see cref="ThingSetMakerByTotalStatUtility.GenerateDefsWithPossibleTotalValue"/>: the loop only
	/// appends to <c>chosen</c>, so a non-empty result is equivalent to a non-empty candidate list after
	/// the first iteration plus a positive weight sum. The pool, threshold and per-pair values are still
	/// evaluated exactly like vanilla; only iterations after the first are skipped. The caller must hold
	/// the same <c>Rand</c> state vanilla uses, because stuff and quality are drawn while building the pool.
	/// </remarks>
	internal static class ThingSetMakerCanGenerate
	{
		internal static bool FirstIterationFeasible(
			ThingSetMakerParams parms,
			float totalValue,
			IEnumerable<ThingDef> allowed,
			Func<ThingStuffPairWithQuality, float> getMinValue,
			Func<ThingStuffPairWithQuality, float> getMaxValue,
			Func<ThingStuffPairWithQuality, float> getSingleThingValue)
		{
			var countRange = parms.countRange ?? new IntRange(1, int.MaxValue);
			if (countRange.max <= 0)
				return false;
			if (countRange.min < 1)
				countRange.min = 1;

			var techLevel = parms.techLevel.GetValueOrDefault();
			var qualityGenerator = parms.qualityGenerator.GetValueOrDefault();
			var maxMass = parms.maxTotalMass ?? float.MaxValue;
			var trackMass = maxMass != float.MaxValue;
			var minSingleItemValue = totalValue * parms.minSingleItemMarketValuePct.GetValueOrDefault();

			var minValues = new Dictionary<ThingStuffPairWithQuality, float>();
			var maxValues = new Dictionary<ThingStuffPairWithQuality, float>();

			float MinValue(ThingStuffPairWithQuality pair)
			{
				if (!minValues.TryGetValue(pair, out var value))
				{
					value = getMinValue(pair);
					minValues[pair] = value;
				}
				return value;
			}

			float MaxValue(ThingStuffPairWithQuality pair)
			{
				if (!maxValues.TryGetValue(pair, out var value))
				{
					value = getMaxValue(pair);
					maxValues[pair] = value;
				}
				return value;
			}

			Func<ThingStuffPairWithQuality, float> minFunc = MinValue;
			Func<ThingStuffPairWithQuality, float> maxFunc = MaxValue;

			ThingSetMakerByTotalStatUtility.CalculateAllowedThingStuffPairs(allowed, techLevel, qualityGenerator);
			var pool = ThingSetMakerByTotalStatUtility.allowedThingStuffPairs;
			var trashThreshold = Mathf.Max(
				ThingSetMakerByTotalStatUtility.GetTrashThreshold(countRange, totalValue, maxFunc),
				minSingleItemValue);
			pool.RemoveAll(x => MaxValue(x) < trashThreshold);
			if (pool.Count == 0)
				return false;

			var minPoolValue = float.MaxValue;
			var maxPoolValue = float.MinValue;
			var minPoolMass = float.MaxValue;
			for (var i = 0; i < pool.Count; i++)
			{
				var pair = pool[i];
				minPoolValue = Mathf.Min(minPoolValue, MinValue(pair));
				maxPoolValue = Mathf.Max(maxPoolValue, MaxValue(pair));
				if (trackMass && pair.thing.category != ThingCategory.Pawn)
					minPoolMass = Mathf.Min(minPoolMass,
						ThingSetMakerByTotalStatUtility.GetNonTrashMass(pair, trashThreshold, minFunc));
			}
			minPoolValue = Mathf.Max(minPoolValue, trashThreshold);

			var candidates = new List<ThingStuffPairWithQuality>();
			for (var i = 0; i < pool.Count; i++)
			{
				var candidate = pool[i];
				// allowNonStackableDuplicates cannot reject anything while chosen is empty.
				if (trackMass && candidate.thing.category != ThingCategory.Pawn)
				{
					var nonTrashMass = ThingSetMakerByTotalStatUtility.GetNonTrashMass(candidate, trashThreshold, minFunc);
					if (nonTrashMass > maxMass
						|| (0 < countRange.min && minPoolMass * (float)(countRange.min - 1) + nonTrashMass > maxMass))
					{
						continue;
					}
				}

				var minCandidateValue = Mathf.Max(MinValue(candidate), trashThreshold);
				if (minCandidateValue > totalValue)
					continue;
				if (0 < countRange.min && minPoolValue * (float)(countRange.min - 1) + minCandidateValue > totalValue)
					continue;

				candidates.Add(candidate);
			}

			if (countRange.max != int.MaxValue && totalValue * 0.5f > 0f)
			{
				var narrowed = new List<ThingStuffPairWithQuality>();
				for (var i = 0; i < candidates.Count; i++)
				{
					var candidate = candidates[i];
					if (maxPoolValue * (float)(countRange.max - 1) + MaxValue(candidate) >= totalValue * 0.5f)
						narrowed.Add(candidate);
				}
				if (narrowed.Count > 0)
					candidates = narrowed;
			}

			if (candidates.Count == 0)
				return false;

			var maxCandidateMinValue = float.MinValue;
			for (var i = 0; i < candidates.Count; i++)
				maxCandidateMinValue = Mathf.Max(maxCandidateMinValue, Mathf.Max(MinValue(candidates[i]), trashThreshold));

			var weightSum = 0f;
			for (var i = 0; i < candidates.Count; i++)
			{
				var weight = Weight(candidates[i]);
				if (weight < 0f)
					weight = 0f;
				weightSum += weight;
			}

			// Mirrors TryRandomElementByWeight: an empty candidate list or a zero weight sum fails.
			return weightSum > 0f;

			float Weight(ThingStuffPairWithQuality x)
			{
				var a2 = 1f;
				if (countRange.max != int.MaxValue && 0 < countRange.max && totalValue >= 0f)
					a2 = Mathf.InverseLerp(0f, totalValue / (float)countRange.max, MaxValue(x));

				var b2 = 1f;
				if (0 < countRange.min && totalValue >= 0f)
				{
					var perItem = totalValue / (float)countRange.min;
					var minValue = Mathf.Max(MinValue(x), trashThreshold);
					if (minValue > perItem)
						b2 = perItem / minValue;
				}

				var weight = Mathf.Max(Mathf.Min(a2, b2), 1E-05f);

				if (totalValue > 0f)
				{
					var count = Mathf.Max(countRange.min, 1);
					var maxWithMass = ThingSetMakerByTotalStatUtility.GetMaxValueWithMaxMass(
						x, 0f, maxMass, minFunc, maxFunc) * (float)count;
					var num13 = Mathf.InverseLerp(0f, maxCandidateMinValue * 0.85f, maxWithMass);
					weight *= num13 * num13;
				}

				if (PawnWeaponGenerator.IsDerpWeapon(x.thing, x.stuff))
					weight *= 0.1f;

				if (techLevel != TechLevel.Undefined)
				{
					var thingTechLevel = x.thing.techLevel;
					if ((int)thingTechLevel < (int)techLevel && (int)thingTechLevel <= 2
						&& (x.thing.IsApparel || x.thing.IsWeapon))
					{
						weight *= 0.1f;
					}
				}

				return weight;
			}
		}
	}
}
