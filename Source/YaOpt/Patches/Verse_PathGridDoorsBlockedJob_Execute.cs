using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PathGridDoorsBlockedJob.Execute"/> with the sparse-clear version that zeroes only
	/// the overlay cells the previous run wrote, instead of clearing the two whole-map arrays per path.
	/// </summary>
	/// <seealso cref="OptimizedDoorsJob"/>
	/// <seealso cref="YaOptSettings.OptPathOverlaySparse"/>
	[HarmonyPatch(typeof(PathGridDoorsBlockedJob))]
	[HarmonyPatch(nameof(PathGridDoorsBlockedJob.Execute))]
	internal static class Verse_PathGridDoorsBlockedJob_Execute
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathOverlaySparse.Enabled;
		}

		static bool Prefix(PathGridDoorsBlockedJob __instance)
		{
			if (__instance == null || __instance.map == null || !__instance.providerCost.IsCreated || !__instance.blocked.IsCreated)
				return true;

			OptimizedDoorsJob.Execute(__instance);
			return false;
		}
	}
}