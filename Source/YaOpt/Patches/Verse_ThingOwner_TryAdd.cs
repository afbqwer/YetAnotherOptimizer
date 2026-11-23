using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Maintains the spawned-things index when a thing is added to <see cref="Map.spawnedThings"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastSpawnedThingsRemove"/>
	[HarmonyPatch(typeof(ThingOwner<Thing>))]
	[HarmonyPatch(nameof(ThingOwner<Thing>.TryAdd), new[] { typeof(Thing), typeof(bool) })]
	internal static class Verse_ThingOwner_TryAdd
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastSpawnedThingsRemove.Enabled;
		}

		static void Postfix(ThingOwner<Thing> __instance, Thing item, bool __result)
		{
			if (__result)
				SpawnedThingsIndexer.OnAdded(__instance, item);
		}
	}
}