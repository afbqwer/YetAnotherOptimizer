using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Drops the spawned-things index when the owner is cleared or the map is deinitialized.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastSpawnedThingsRemove"/>
	[HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.Clear))]
	internal static class Verse_ThingOwner_Clear
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastSpawnedThingsRemove.Enabled;
		}

		static void Postfix(ThingOwner __instance)
		{
			SpawnedThingsIndexer.OnCleared(__instance);
		}
	}
}