using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Locks YaOpt render optimizations while WOWGAG's Performance Patch has an overlapping patch attached.
	/// </summary>
	/// <remarks>
	/// The state is read from Harmony patch info, so toggling a GPP feature on or off is reflected by
	/// the actual patch presence rather than by its saved setting value.
	/// </remarks>
	internal static class GppCompatibility
	{
		private const string GppHarmonyPrefix = "wowgag.guiperformancepatch";
		private const string MaterialReason = "WOWGAG's Performance Patch (pawn material patches)";
		private const string CorpseReason = "WOWGAG's Performance Patch (reuse corpse rendering)";

		private static int _lastUiRefreshFrame = -1;

		private static readonly MethodInfo GetMaterial = AccessTools.Method(typeof(PawnRenderNodeWorker),
			nameof(PawnRenderNodeWorker.GetMaterialPropertyBlock));

		private static readonly MethodInfo PreDraw = AccessTools.Method(typeof(PawnRenderNodeWorker),
			nameof(PawnRenderNodeWorker.PreDraw));

		private static readonly MethodInfo ParallelPreDraw = AccessTools.Method(typeof(PawnRenderTree),
			nameof(PawnRenderTree.ParallelPreDraw));

		internal static bool Refresh()
		{
			var materialConflict = HasGppMaterialPatch(GetMaterial) || HasGppMaterialPatch(PreDraw);
			var corpseConflict = HasGppPatchType(ParallelPreDraw, "GUIPerformancePatch.GppRenderOpt");

			var changed = SetBan(nameof(YaOptSettings.OptParallelMaterialUpdate), materialConflict,
				MaterialReason);
			changed |= SetBan(nameof(YaOptSettings.OptReuseCorpseRender), corpseConflict, CorpseReason);
			return changed;
		}

		/// <summary>
		/// Re-checks attached patches while the settings window is open and reapplies YaOpt patches when
		/// the lock state changes, so turning a GPP counterpart off makes the YaOpt option usable.
		/// </summary>
		internal static void RefreshFromSettingsWindow()
		{
			var frame = UnityEngine.Time.frameCount;
			if (frame == _lastUiRefreshFrame)
				return;
			_lastUiRefreshFrame = frame;
			if (Refresh())
				YaOpt.Patches.Patcher.TryPatch(true, false);
		}

		private static bool HasGppMaterialPatch(MethodBase target)
		{
			var info = target == null ? null : Harmony.GetPatchInfo(target);
			if (info == null)
				return false;

			foreach (var patch in info.Transpilers)
			{
				if (IsGppOwner(patch))
					return true;
			}

			return HasGppPatchType(info, "GUIPerformancePatch.GppMatFacts") ||
				HasGppPatchType(info, "GUIPerformancePatch.GppMatColorId") ||
				HasGppPatchType(info, "GUIPerformancePatch.GppPreDrawSkip");
		}

		private static bool HasGppPatchType(MethodBase target, string typeName)
		{
			var info = target == null ? null : Harmony.GetPatchInfo(target);
			return info != null && HasGppPatchType(info, typeName);
		}

		private static bool HasGppPatchType(HarmonyLib.Patches info, string typeName)
		{
			return HasGppPatchType(info.Prefixes, typeName) ||
				HasGppPatchType(info.Postfixes, typeName) ||
				HasGppPatchType(info.Transpilers, typeName);
		}

		private static bool HasGppPatchType(IEnumerable<Patch> patches, string typeName)
		{
			if (patches == null)
				return false;

			foreach (var patch in patches)
			{
				if (patch?.PatchMethod?.DeclaringType?.FullName == typeName)
					return true;
			}
			return false;
		}

		private static bool IsGppOwner(Patch patch)
		{
			return patch?.owner != null &&
				patch.owner.StartsWith(GppHarmonyPrefix, System.StringComparison.Ordinal);
		}

		private static bool SetBan(string settingId, bool conflict, string reason)
		{
			if (!conflict)
				return CompatibilityDefines.Unban(settingId);

			if (CompatibilityDefines.CachedBannedOptimizations.Contains(settingId) &&
				CompatibilityDefines.CachedBannedBy.TryGetValue(settingId, out var existing) &&
				existing == reason)
				return false;

			CompatibilityDefines.Ban(settingId, reason);
			return true;
		}
	}
}