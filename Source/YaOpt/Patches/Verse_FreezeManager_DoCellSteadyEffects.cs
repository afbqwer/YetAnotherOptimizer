using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the per-cell freeze/melt pass with a merged implementation that shares the temperature
	/// read and defers room lookups.
	/// </summary>
	/// <remarks>
	/// The replacement preserves the vanilla random order: freezing rolls first, melting second, each
	/// under the same temperature and terrain conditions. Cells outside the freeze/melt temperature
	/// band no longer resolve a room at all.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTempTerrainMerge"/>
	/// <seealso cref="TempTerrainMergeHelper"/>
	[HarmonyPatch(typeof(FreezeManager), nameof(FreezeManager.DoCellSteadyEffects))]
	internal static class Verse_FreezeManager_DoCellSteadyEffects
	{
		static bool Prepare()
		{
			return ModsConfig.OdysseyActive &&
				YaOptGlobal.Settings?.OptTempTerrainMerge.Enabled == true;
		}

		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(FreezeManager __instance, IntVec3 c)
		{
			if (!ModsConfig.OdysseyActive || __instance.map == null)
				return true;

			TempTerrainMergeHelper.DoCell(__instance, c);
			return false;
		}
	}
}