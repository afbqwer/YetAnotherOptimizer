using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <seealso cref="YaOptSettings.OptListerBuildingsCache"/>
	[HarmonyPatch(typeof(ListerBuildings))]
	[HarmonyPatch(nameof(ListerBuildings.Add))]
	internal static class Verse_ListerBuildings_Add
	{
		static bool Prepare()
		{
			return PrepatcherState.ListerBuildingsActive;
		}

		static void Postfix(ListerBuildings __instance, Building b, bool __runOriginal)
		{
			if (__runOriginal)
				ListerBuildingsCache.OnAdded(__instance, b);
		}
	}

	/// <seealso cref="YaOptSettings.OptListerBuildingsCache"/>
	[HarmonyPatch(typeof(ListerBuildings))]
	[HarmonyPatch(nameof(ListerBuildings.Remove))]
	internal static class Verse_ListerBuildings_Remove
	{
		static bool Prepare()
		{
			return PrepatcherState.ListerBuildingsActive;
		}

		static void Postfix(ListerBuildings __instance, Building b, bool __runOriginal)
		{
			if (__runOriginal)
				ListerBuildingsCache.OnRemoved(__instance, b);
		}
	}

	/// <seealso cref="YaOptSettings.OptListerBuildingsCache"/>
	[HarmonyPatch(typeof(ListerBuildings))]
	[HarmonyPatch(nameof(ListerBuildings.Dispose))]
	internal static class Verse_ListerBuildings_Dispose
	{
		static bool Prepare()
		{
			return PrepatcherState.ListerBuildingsActive;
		}

		static void Postfix(ListerBuildings __instance, bool __runOriginal)
		{
			if (__runOriginal)
				ListerBuildingsCache.OnDisposed(__instance);
		}
	}
}