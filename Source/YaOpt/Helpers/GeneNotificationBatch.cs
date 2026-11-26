using System;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Defers the idempotent global invalidation triggered by each gene during one generation.
	/// </summary>
	/// <remarks>
	/// <see cref="Pawn_GeneTracker.Notify_GenesChanged"/> still runs its per-gene skin, hair, body, head,
	/// fur, conflict and need logic. Only the global parts are batched: the heavy health capacity and
	/// summary recompute, the work-settings recalculation and the graphics dirty call. Health caches that
	/// other gene code may read are still rebuilt in the lightweight path; wide invalidations that only
	/// matter after generation runs once when the batch closes. The batch is opened only for
	/// <c>PawnGenerator.GenerateGenes</c>, never for runtime gene edits.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	internal static class GeneNotificationBatch
	{
		[ThreadStatic]
		private static bool _active;

		[ThreadStatic]
		private static Pawn _pawn;

		[ThreadStatic]
		private static bool _hediffDirty;

		[ThreadStatic]
		private static bool _workTypesDirty;

		[ThreadStatic]
		private static bool _graphicsDirty;

		/// <summary>
		/// Opens a batch for a fresh pawn generation. Returns false for nested or non-gene pawns.
		/// </summary>
		internal static bool Begin(Pawn pawn)
		{
			if (_active || pawn?.genes == null)
				return false;

			_active = true;
			_pawn = pawn;
			_hediffDirty = false;
			_workTypesDirty = false;
			_graphicsDirty = false;
			return true;
		}

		internal static void End(bool state)
		{
			if (!state)
				return;

			var pawn = _pawn;
			var hediffDirty = _hediffDirty;
			var workTypesDirty = _workTypesDirty;
			var graphicsDirty = _graphicsDirty;

			_active = false;
			_pawn = null;
			_hediffDirty = false;
			_workTypesDirty = false;
			_graphicsDirty = false;

			if (hediffDirty && pawn?.health?.hediffSet != null)
				pawn.health.hediffSet.DirtyCache();
			if (pawn == null)
				return;
			if (workTypesDirty)
				pawn.Notify_DisabledWorkTypesChanged();
			if (graphicsDirty && pawn.Drawer?.renderer != null)
				pawn.Drawer.renderer.SetAllGraphicsDirty();
		}

		internal static bool ShouldBatch(HediffSet hediffSet)
		{
			return _active && hediffSet?.pawn == _pawn;
		}

		internal static bool ShouldBatch(Pawn pawn)
		{
			return _active && pawn == _pawn;
		}

		internal static bool ShouldBatch(PawnRenderer renderer)
		{
			return _active && renderer?.pawn == _pawn;
		}

		/// <summary>
		/// Runs the cache-rebuilding half of <see cref="HediffSet.DirtyCache"/> and leaves the wide
		/// graphics, portrait, atlas, capacity and summary invalidation for the final flush.
		/// </summary>
		internal static void MarkHediffDirty(HediffSet hediffSet)
		{
			_hediffDirty = true;
			hediffSet.CacheMissingPartsCommonAncestors();
			hediffSet.CacheThoughtsNullified();
			hediffSet.CacheNeeds();
			hediffSet.cachedPain = -1f;
			hediffSet.cachedBleedRate = -1f;
			hediffSet.cachedHasHead = null;
			hediffSet.cachedCanCrawl = null;
			hediffSet.cachedHasRegeneration = null;
			hediffSet.cachedRemoveRoamMtb = null;
			hediffSet.cachedPreventVacuumBurns = null;
			hediffSet.cachedDamageFactors.Clear();
			hediffSet.pawn.health.capacities.Notify_CapacityLevelsDirty();
			hediffSet.pawn.health.summaryHealth.Notify_HealthChanged();
		}

		/// <summary>
		/// Invalidates the work-type caches but keeps the expensive work-settings recalculation for the
		/// final flush, so reads during the batch still compute fresh values.
		/// </summary>
		internal static void MarkWorkTypesDirty(Pawn pawn)
		{
			_workTypesDirty = true;
			pawn.cachedDisabledWorkTypes = null;
			pawn.cachedDisabledWorkTypesPermanent = null;
			pawn.cachedReasonsForDisabledWorkTypes = null;
			pawn.skills?.Notify_SkillDisablesChanged();
		}

		internal static void MarkGraphicsDirty()
		{
			_graphicsDirty = true;
		}
	}
}