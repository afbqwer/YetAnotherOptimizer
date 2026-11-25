using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Section-indexed replacement for the O(lights) scan in <see cref="GlowGrid.DirtyLightsAround"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DirtyLightsAround))]
	internal static class Verse_GlowGrid_DirtyLightsAround
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static bool Prefix(GlowGrid __instance, IntVec3 cell)
		{
			return !GlowGridOptimizer.DirtyLightsAround(__instance, cell);
		}
	}
}
