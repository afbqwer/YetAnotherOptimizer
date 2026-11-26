using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes <see cref="StatDefOf.ImmunityGainSpeed"/> lookups through the vanilla temporary stat
	/// cache with a 20-tick interval.
	/// </summary>
	/// <remarks>
	/// <see cref="ImmunityRecord.ImmunityChangePerTick"/> evaluates the full stat pipeline for every
	/// immunity record on every health interval. The value depends on food, rest, bed, resting and
	/// age, so a short window is used. A recent food, rest, bed or age change may take up to 20 ticks
	/// to affect immunity gain. The matching XML patch makes the stat cacheable.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptImmunityStatCache"/>
	/// <seealso cref="Patches.Xml.PatchOperationSettingGate"/>
	[HarmonyPatch(typeof(StatWorker))]
	[HarmonyPatch(nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
	internal static class RimWorld_StatWorker_GetValue_ImmunityStatCache
	{
		private const int CacheInterval = 20;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptImmunityStatCache.Enabled == true;
		}

		static void Prefix(StatDef ___stat, Thing thing, ref int cacheStaleAfterTicks)
		{
			if (cacheStaleAfterTicks != -1 || ___stat != StatDefOf.ImmunityGainSpeed)
				return;
			if (!(thing is Pawn pawn) || !pawn.Spawned)
				return;

			cacheStaleAfterTicks = CacheInterval;
		}
	}
}