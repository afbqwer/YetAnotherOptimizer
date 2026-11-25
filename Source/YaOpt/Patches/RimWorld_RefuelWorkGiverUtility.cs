using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches the best-fuel search of <see cref="RefuelWorkGiverUtility"/> for the current tick.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptRefuelSearchCache"/>
	[HarmonyPatch(typeof(RefuelWorkGiverUtility))]
	[HarmonyPatch(nameof(RefuelWorkGiverUtility.FindBestFuel))]
	internal static class RimWorld_RefuelWorkGiverUtility_FindBestFuel
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptRefuelSearchCache.Enabled;
		}

		static bool Prefix(Pawn pawn, Thing refuelable, ref Thing __result)
		{
			return !RefuelSearchCache.TryGetBestFuel(pawn, refuelable, out __result);
		}

		static void Postfix(Pawn pawn, Thing refuelable, bool __runOriginal, ref Thing __result)
		{
			if (__runOriginal)
				RefuelSearchCache.StoreBestFuel(pawn, refuelable, __result);
		}
	}

	/// <summary>
	/// Caches the all-fuel traversal of <see cref="RefuelWorkGiverUtility"/> for the current tick.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptRefuelSearchCache"/>
	[HarmonyPatch(typeof(RefuelWorkGiverUtility))]
	[HarmonyPatch(nameof(RefuelWorkGiverUtility.FindAllFuel))]
	internal static class RimWorld_RefuelWorkGiverUtility_FindAllFuel
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptRefuelSearchCache.Enabled;
		}

		static bool Prefix(Pawn pawn, Thing refuelable, ref List<Thing> __result)
		{
			return !RefuelSearchCache.TryGetAllFuel(pawn, refuelable, out __result);
		}

		static void Postfix(Pawn pawn, Thing refuelable, bool __runOriginal, ref List<Thing> __result)
		{
			if (__runOriginal)
				RefuelSearchCache.StoreAllFuel(pawn, refuelable, __result);
		}
	}
}