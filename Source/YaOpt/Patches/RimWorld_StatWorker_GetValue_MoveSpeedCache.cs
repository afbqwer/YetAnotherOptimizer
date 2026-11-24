using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes the <see cref="Pawn.TicksPerMove"/> MoveSpeed lookup through the vanilla temporary
	/// stat cache with a 20-tick interval. Vanilla evaluates <c>GetStatValue(StatDefOf.MoveSpeed)</c>
	/// once per cell walked (via <c>CostToMoveIntoCell</c>) and passes no <c>cacheStaleAfterTicks</c>,
	/// so every cell re-runs the full stat evaluation. The matching XML patch makes MoveSpeed
	/// cacheable; this prefix sets the interval at the single hot call site.
	/// </summary>
	/// <remarks>
	/// Only <see cref="StatDefOf.MoveSpeed"/> is touched. <c>TicksPerMove</c> also reads
	/// <see cref="StatDefOf.CrawlSpeed"/> for downed crawlers; that lookup is left alone because this
	/// prefix matches on the stat, not the caller.
	/// <para/>
	/// Revenants are excluded: <see cref="StatPart_RevenantSpeed"/> derives its factor from
	/// <c>TicksGame - lastBecameVisibleTick</c>, so a cached value would lag the visibility curve.
	/// </remarks>
	/// <seealso cref="Patches.Xml.PatchOperationSettingGate"/>
	/// <seealso cref="YaOptSettings.OptMoveSpeedStatCache"/>
	[HarmonyPatch(typeof(StatWorker))]
	[HarmonyPatch(nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int))]
	internal static class RimWorld_StatWorker_GetValue_MoveSpeedCache
	{
		// 20 ticks keeps the staleness window short enough that a mid-walk leg break
		// (Moving capacity drop) becomes visible within a third of a second.
		private const int CacheInterval = 20;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptMoveSpeedStatCache.Enabled == true;
		}

		static void Prefix(StatDef ___stat, Thing thing, ref int cacheStaleAfterTicks)
		{
			if (cacheStaleAfterTicks != -1 || ___stat != StatDefOf.MoveSpeed)
				return;
			if (!(thing is Pawn pawn) || !pawn.Spawned)
				return;
			if (ModsConfig.AnomalyActive && pawn.kindDef == PawnKindDefOf.Revenant)
				return;
			cacheStaleAfterTicks = CacheInterval;
		}
	}
}
