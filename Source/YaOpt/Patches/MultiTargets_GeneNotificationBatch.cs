using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Defers the wide notification calls made by <see cref="Pawn_GeneTracker.Notify_GenesChanged"/>.
	/// </summary>
	/// <remarks>
	/// The per-gene visual and conflict logic is untouched. Only the idempotent global invalidation is
	/// captured while <see cref="GeneNotificationBatch"/> is open; the batch then calls the original
	/// methods once. A foreign patch on any target disables the batch during refresh.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	[HarmonyPatch]
	internal static class MultiTargets_GeneNotificationBatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var dirtyCache = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache));
			var notifyWorkTypes = AccessTools.Method(typeof(Pawn),
				nameof(Pawn.Notify_DisabledWorkTypesChanged));
			var graphicsDirty = AccessTools.Method(typeof(PawnRenderer),
				nameof(PawnRenderer.SetAllGraphicsDirty));

			if (dirtyCache != null)
				yield return dirtyCache;
			if (notifyWorkTypes != null)
				yield return notifyWorkTypes;
			if (graphicsDirty != null)
				yield return graphicsDirty;
		}

		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled && PawnGenerationIndexCompat.GeneBatchReady;
		}

		static bool Prefix(object __instance)
		{
			if (!PawnGenerationIndexCompat.MasterEnabled || !PawnGenerationIndexCompat.GeneBatchReady)
				return true;

			if (__instance is HediffSet hediffSet)
			{
				if (!GeneNotificationBatch.ShouldBatch(hediffSet))
					return true;

				GeneNotificationBatch.MarkHediffDirty(hediffSet);
				return false;
			}

			if (__instance is Pawn pawn)
			{
				if (!GeneNotificationBatch.ShouldBatch(pawn))
					return true;

				GeneNotificationBatch.MarkWorkTypesDirty(pawn);
				return false;
			}

			if (__instance is PawnRenderer renderer)
			{
				if (!GeneNotificationBatch.ShouldBatch(renderer))
					return true;

				GeneNotificationBatch.MarkGraphicsDirty();
				return false;
			}

			return true;
		}
	}
}