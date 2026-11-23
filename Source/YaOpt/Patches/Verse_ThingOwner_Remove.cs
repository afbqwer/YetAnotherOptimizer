using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Uses the spawned-things index for O(1) removals from <see cref="Map.spawnedThings"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastSpawnedThingsRemove"/>
	[HarmonyPatch(typeof(ThingOwner<Thing>), nameof(ThingOwner<Thing>.Remove))]
	internal static class Verse_ThingOwner_Remove
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastSpawnedThingsRemove.Enabled;
		}

		static bool Prefix(ThingOwner<Thing> __instance, Thing item, ref bool __result)
		{
			return !SpawnedThingsIndexer.TryRemove(__instance, item, out __result);
		}
	}
}