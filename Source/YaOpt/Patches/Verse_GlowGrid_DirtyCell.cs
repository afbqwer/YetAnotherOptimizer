using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Records dirtied cells in the compact list consumed by <see cref="GlowGridOptimizer"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DirtyCell))]
	internal static class Verse_GlowGrid_DirtyCell
	{
		static bool Prepare()
		{
			GlowGridOptimizer.Reset();
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance, IntVec3 cell)
		{
			// DirtyCell itself is a no-op before the first full run; the initial pass covers it.
			if (!__instance.hasRunInitially)
			{
				return;
			}

			GlowGridOptimizer.MarkDirty(__instance, cell.x, cell.z);
		}
	}
}
