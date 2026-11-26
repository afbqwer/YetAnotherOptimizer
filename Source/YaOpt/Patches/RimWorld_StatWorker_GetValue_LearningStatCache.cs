using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes <see cref="StatDefOf.GlobalLearningFactor"/> lookups through the vanilla temporary stat
	/// cache with a 120-tick interval.
	/// </summary>
	/// <remarks>
	/// <see cref="SkillRecord.LearnRateFactor"/> evaluates the full stat pipeline on every Learn call,
	/// even though the value changes only with genes, hediffs, traits, apparel, equipment or ideo
	/// state. The matching XML patch makes the stat cacheable; this prefix supplies the interval at
	/// every <see cref="StatWorker.GetValue(Thing, bool, int)"/> call site that leaves
	/// <c>cacheStaleAfterTicks</c> at its default. A recent change may take up to 120 ticks (about two
	/// seconds) to affect learning speed.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptLearningStatCache"/>
	/// <seealso cref="Patches.Xml.PatchOperationSettingGate"/>
	[HarmonyPatch(typeof(StatWorker))]
	[HarmonyPatch(nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
	internal static class RimWorld_StatWorker_GetValue_LearningStatCache
	{
		private const int CacheInterval = 120;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLearningStatCache.Enabled == true;
		}

		static void Prefix(StatDef ___stat, Thing thing, ref int cacheStaleAfterTicks)
		{
			if (cacheStaleAfterTicks != -1 || ___stat != StatDefOf.GlobalLearningFactor)
				return;
			if (!(thing is Pawn pawn) || !pawn.Spawned)
				return;

			cacheStaleAfterTicks = CacheInterval;
		}
	}
}