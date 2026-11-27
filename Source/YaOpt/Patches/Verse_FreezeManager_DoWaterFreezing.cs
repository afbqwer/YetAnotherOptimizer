using HarmonyLib;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips water freezing on cells whose terrain can never freeze.
	/// </summary>
	/// <remarks>
	/// Vanilla reads the cell temperature and room before checking <c>TerrainDef.canFreeze</c>.
	/// The terrain check is decisive and cheaper, so non-freezable cells can skip the rest of
	/// the freezing pass; freezable cells still run vanilla unchanged.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTempTerrainPrune"/>
	[HarmonyPatch(typeof(FreezeManager), "DoWaterFreezing")]
	internal static class Verse_FreezeManager_DoWaterFreezing
	{
		static bool Prepare()
		{
			return ModsConfig.OdysseyActive &&
				!YaOptGlobal.Settings.OptTempTerrainMerge.Enabled &&
				YaOptGlobal.Settings.OptTempTerrainPrune.Enabled;
		}

		static bool Prefix(FreezeManager __instance, IntVec3 c)
		{
			return c.GetTerrain(__instance.map).canFreeze;
		}
	}
}
