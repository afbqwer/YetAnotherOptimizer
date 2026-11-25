using System;
using HarmonyLib;
using RimWorld;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises the value callbacks of <c>GenerateDefsWithPossibleTotalValue</c> for one call.
	/// </summary>
	/// <remarks>
	/// Vanilla evaluates the same stat up to three times per candidate (median threshold, minimum value
	/// and single-item value). Only vanilla callbacks are wrapped, and the per-call dictionaries are
	/// discarded afterwards, so nothing leaks between generations.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptThingSetMakerStatCache"/>
	/// <seealso cref="GenerationStatMemo"/>
	[HarmonyPatch(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GenerateDefsWithPossibleTotalValue))]
	internal static class RimWorld_ThingSetMakerByTotalStatUtility_GenerateDefsWithPossibleTotalValue
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptThingSetMakerStatCache.Enabled == true;
		}

		static void Prefix(ref Func<ThingStuffPairWithQuality, float> getMinValue,
			ref Func<ThingStuffPairWithQuality, float> getMaxValue,
			ref Func<ThingStuffPairWithQuality, float> getSingleThingValue)
		{
			getMinValue = GenerationStatMemo.Wrap(getMinValue);
			getMaxValue = GenerationStatMemo.Wrap(getMaxValue);
			getSingleThingValue = GenerationStatMemo.Wrap(getSingleThingValue);
		}
	}
}
