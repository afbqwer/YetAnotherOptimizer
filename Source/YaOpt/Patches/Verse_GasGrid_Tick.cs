using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the fixed-size gas grid sweeps with a bitmap-driven skip.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGasGridTick"/>
	[HarmonyPatch(typeof(GasGrid))]
	[HarmonyPatch(nameof(GasGrid.Tick))]
	internal static class Verse_GasGrid_Tick
	{
		static bool Prepare()
		{
			GasGridOptimizer.Reset();
			return YaOptGlobal.Settings.OptGasGridTick.Enabled;
		}

		static bool Prefix(GasGrid __instance)
		{
			return !GasGridOptimizer.Tick(__instance);
		}
	}
}
