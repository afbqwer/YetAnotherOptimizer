using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Rebuilds the packed invisibility state when a pawn enters the map.
	/// </summary>
	/// <remarks>
	/// Covers everything the event patches cannot see: loading a save (the injected field starts at
	/// zero even for pawns that already carry an invisibility hediff), resurrection, and any mod
	/// that rebuilds the hediff list directly.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(Thing))]
	[HarmonyPatch(nameof(Thing.SpawnSetup))]
	internal static class Verse_Pawn_InvisibilitySpawnSetup
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static void Postfix(Thing __instance)
		{
			if (__instance is Pawn pawn)
				PawnInvisibilityCache.Rebuild(pawn);
		}
	}
}
