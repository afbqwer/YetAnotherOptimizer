using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the glow combine pass with a section-indexed implementation.
	/// </summary>
	/// <remarks>
	/// The prefix returns false (<see cref="GlowGridOptimizer.Update"/> handled the whole method).
	/// The vanilla flood fill is still scheduled from inside the helper, so per-light local colors
	/// remain identical; only the O(cells x lights) combine is replaced.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.GlowGridUpdate_First))]
	internal static class Verse_GlowGrid_GlowGridUpdate_First
	{
		static bool Prepare()
		{
			GlowGridOptimizer.Reset();
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static bool Prefix(GlowGrid __instance)
		{
			// false: skip vanilla when the helper handled the update; true: fall through when there is
			// nothing to combine and vanilla would no-op anyway.
			return !GlowGridOptimizer.Update(__instance);
		}
	}
}
