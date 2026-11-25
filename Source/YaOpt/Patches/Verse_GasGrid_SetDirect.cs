using System;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Keeps the gas occupancy bitmap in sync with the density array.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGasGridTick"/>
	[HarmonyPatch(typeof(GasGrid))]
	[HarmonyPatch(nameof(GasGrid.SetDirect),
		new[] { typeof(int), typeof(byte), typeof(byte), typeof(byte), typeof(byte) })]
	internal static class Verse_GasGrid_SetDirect_Densities
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGasGridTick.Enabled;
		}

		static void Postfix(GasGrid __instance, int index)
		{
			GasGridOptimizer.NotifyWrite(__instance, index);
		}
	}

	/// <summary>
	/// Keeps the gas occupancy bitmap in sync with the packed density array.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGasGridTick"/>
	[HarmonyPatch(typeof(GasGrid))]
	[HarmonyPatch(nameof(GasGrid.SetDirect), new[] { typeof(int), typeof(uint) })]
	internal static class Verse_GasGrid_SetDirect_Packed
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGasGridTick.Enabled;
		}

		static void Postfix(GasGrid __instance, int index)
		{
			GasGridOptimizer.NotifyWrite(__instance, index);
		}
	}

	/// <summary>
	/// Vectorized scan for "is there any gas left at all".
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGasGridTick"/>
	[HarmonyPatch(typeof(GasGrid))]
	[HarmonyPatch(nameof(GasGrid.RecalculateEverHadGas))]
	internal static class Verse_GasGrid_RecalculateEverHadGas
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGasGridTick.Enabled;
		}

		static bool Prefix(GasGrid __instance)
		{
			var area = __instance.map.Area;
			__instance.anyGasEverAdded =
				__instance.gasDensity.AsSpan(0, area).IndexOfAnyExcept(0u) >= 0;
			return false;
		}
	}
}
