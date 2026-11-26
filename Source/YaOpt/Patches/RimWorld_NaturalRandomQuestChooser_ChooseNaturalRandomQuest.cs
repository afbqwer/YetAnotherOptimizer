using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the weighted full-root table with a weighted random order and early CanRun exit.
	/// </summary>
	/// <remarks>
	/// This path keeps the population-increase coin and the natural selection weight. Eligible roots get a
	/// weighted random order first, then CanRun runs in that order until the first valid root. The result
	/// remains proportional to the natural selection weight, while roots after the first valid one avoid the
	/// expensive CanRun call. The option is disabled when another mod already owns this method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptQuestSelectionFastPath"/>
	/// <seealso cref="QuestSelectionUtility"/>
	/// <seealso cref="QuestGenerationOptimizationCompat"/>
	[HarmonyPatch]
	internal static class RimWorld_NaturalRandomQuestChooser_ChooseNaturalRandomQuest
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(NaturalRandomQuestChooser),
				nameof(NaturalRandomQuestChooser.ChooseNaturalRandomQuest),
				new[] { typeof(float), typeof(IIncidentTarget) });
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptQuestSelectionFastPath.Enabled == true;
		}

		static bool Prefix(float points, IIncidentTarget target, ref QuestScriptDef __result)
		{
			if (target == null)
				return true;

			if (points <= QuestPointsUtility.Epsilon)
			{
				var autoPoints = QuestPointsUtility.ComputeThreatPointsSafe(target);
				if (autoPoints > QuestPointsUtility.Epsilon)
				{
					points = autoPoints;
					QuestPointsUtility.RecordAutoPoints(target, autoPoints);
				}
			}

			var incPop = Rand.Chance(NaturalRandomQuestChooser.PopulationIncreasingQuestChance());
			if (QuestSelectionUtility.TryChooseFast(incPop, points, target, out var chosen)
				|| (incPop && QuestSelectionUtility.TryChooseFast(false, points, target, out chosen)))
			{
				__result = chosen;
				return false;
			}

			if (YaOptGlobal.Settings?.OptQuestAncientComplexFallback.Enabled == true
				&& QuestSelectionUtility.TryGetAncientComplexFallback(points, target, out chosen))
			{
				__result = chosen;
				return false;
			}

			__result = null;
			return false;
		}
	}
}