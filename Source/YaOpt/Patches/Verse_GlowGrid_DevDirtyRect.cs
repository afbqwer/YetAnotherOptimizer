using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Records debug-dirtied cells in the compact list. <c>DevDirtyRect</c> writes the vanilla dirty
	/// bitmap directly, bypassing <see cref="Verse.GlowGrid.DirtyCell"/> and therefore the postfix
	/// that normally feeds <see cref="Helpers.GlowGridOptimizer"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DevDirtyRect))]
	internal static class Verse_GlowGrid_DevDirtyRect
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance, CellRect rect)
		{
			if (!__instance.hasRunInitially)
			{
				return;
			}

			foreach (var cell in rect)
			{
				if (__instance.indices.Contains(cell))
				{
					GlowGridOptimizer.MarkDirty(__instance, cell.x, cell.z);
				}
			}
		}
	}
}