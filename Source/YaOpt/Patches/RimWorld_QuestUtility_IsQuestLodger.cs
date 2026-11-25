using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the two-pass home/mini-faction lookup with one scan for either faction type.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptQuestLodgerFastPath"/>
	[HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.IsQuestLodger), typeof(Pawn))]
	internal static class RimWorld_QuestUtility_IsQuestLodger
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptQuestLodgerFastPath.Enabled == true;
		}

		static bool Prefix(Pawn p, ref bool __result)
		{
			if (p == null)
				return true;

			var parts = Find.QuestManager.ExtraFactionQuestParts;
			for (var i = 0; i < parts.Count; i++)
			{
				var part = parts[i];
				var extraFaction = part.extraFaction;
				if (extraFaction?.faction == null)
					continue;

				var quest = part.quest;
				if (quest.State != QuestState.Ongoing
					&& !(quest.root == QuestScriptDefOf.Hospitality_Refugee && !p.IsColonist))
				{
					continue;
				}

				if (!part.affectedPawns.Contains(p))
					continue;

				if (extraFaction.factionType == ExtraFactionType.HomeFaction
					|| extraFaction.factionType == ExtraFactionType.MiniFaction)
				{
					__result = true;
					return false;
				}
			}

			__result = false;
			return false;
		}
	}
}
