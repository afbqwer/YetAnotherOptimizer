using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Selects a natural random quest root by weighted random order and early CanRun exit.
	/// </summary>
	/// <remarks>
	/// Building a weight table and checking every root with CanRun is the expensive part of vanilla
	/// selection. This path assigns each eligible root an exponential-key sample
	/// <c>-log(U) / weight</c>, sorts by that key to obtain a weighted random permutation, and then runs
	/// CanRun in that order until the first valid root. The first valid root has probability proportional
	/// to <see cref="NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight"/>, but roots after the first
	/// valid one are never tested. If none is valid, every root is tested once. Roots accepted by the
	/// current planet layer are checked first; a weighted fallback order is used only when none passes, so
	/// a layer restriction cannot turn a valid quest into no quest.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptQuestSelectionFastPath"/>
	/// <seealso cref="QuestRootCache"/>
	internal static class QuestSelectionUtility
	{
		private struct Candidate
		{
			internal QuestScriptDef Def;

			internal float Key;
		}

		private static readonly Comparison<Candidate> CandidateComparison = CompareCandidates;

		private static readonly string[] AncientComplexDefNames =
		{
			"OpportunitySite_AncientComplex",
			"OpportunitySite_AncientComplex_Giver",
			"OpportunitySite_AncientComplex_Mechanitor",
			"AncientComplex_Standard"
		};

		internal static bool TryChooseFast(bool incPop, float points, IIncidentTarget target,
			out QuestScriptDef chosen)
		{
			chosen = null;
			if (target == null)
				return false;

			var roots = QuestRootCache.GetRoots(incPop);
			var count = roots.Count;
			if (count == 0)
				return false;

			var preferred = new List<Candidate>(count);
			var layerFallback = new List<Candidate>(count);
			var storyState = target.StoryState;

			for (var i = 0; i < count; i++)
			{
				var root = roots[i];
				if (root == null)
					continue;

				var weight = NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(root, points, storyState);
				if (!(weight > 0f))
					continue;

				// Exponential race key: the smallest key wins with probability weight / totalWeight.
				var key = (float)(-Math.Log(1.0 - Rand.Value) / weight);
				var candidate = new Candidate { Def = root, Key = key };
				(PassesLayerGate(root, target) ? preferred : layerFallback).Add(candidate);
			}

			if (TryPick(preferred, points, target, out chosen))
				return true;

			return TryPick(layerFallback, points, target, out chosen);
		}

		private static bool TryPick(List<Candidate> candidates, float points, IIncidentTarget target,
			out QuestScriptDef chosen)
		{
			chosen = null;
			if (candidates.Count == 0)
				return false;

			candidates.Sort(CandidateComparison);
			for (var i = 0; i < candidates.Count; i++)
			{
				var candidate = candidates[i];
				if (candidate.Def.CanRun(points, target))
				{
					chosen = candidate.Def;
					return true;
				}
			}

			return false;
		}

		private static int CompareCandidates(Candidate a, Candidate b)
		{
			return a.Key.CompareTo(b.Key);
		}

		private static bool PassesLayerGate(QuestScriptDef quest, IIncidentTarget target)
		{
			var tile = target.Tile;
			if (!tile.Valid)
				return true;

			var layerDef = tile.LayerDef;
			if (layerDef == null)
				return true;

			var whitelist = quest.layerWhitelist;
			var whitelistEmpty = whitelist == null || whitelist.Count == 0;
			if (layerDef.onlyAllowWhitelistedQuests && (whitelistEmpty || !whitelist.Contains(layerDef)))
				return false;

			if (!quest.canOccurOnAllPlanetLayers && layerDef.onlyAllowWhitelistedIncidents
				&& (whitelistEmpty || !whitelist.Contains(layerDef)))
			{
				return false;
			}

			return true;
		}

		internal static bool TryGetAncientComplexFallback(float points, IIncidentTarget target,
			out QuestScriptDef chosen)
		{
			chosen = null;
			if (target == null)
				return false;

			for (var i = 0; i < AncientComplexDefNames.Length; i++)
			{
				var def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(AncientComplexDefNames[i]);
				if (def != null && def.CanRun(points, target))
				{
					chosen = def;
					return true;
				}
			}

			var allDefs = DefDatabase<QuestScriptDef>.AllDefsListForReading;
			for (var i = 0; i < allDefs.Count; i++)
			{
				var def = allDefs[i];
				if (def == null || def.defName.NullOrEmpty()
					|| def.defName.IndexOf("AncientComplex", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				if (def.CanRun(points, target))
				{
					chosen = def;
					return true;
				}
			}

			return false;
		}

		}
}