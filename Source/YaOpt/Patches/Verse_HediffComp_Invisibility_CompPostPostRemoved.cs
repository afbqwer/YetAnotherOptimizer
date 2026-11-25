using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Rebuilds the packed invisibility state when an invisibility comp is removed.
	/// </summary>
	/// <remarks>
	/// Removal is the only path that can strand a pawn in a wrong state forever: nothing else
	/// clears the presence bit. <see cref="Pawn.SpawnSetup"/> provides a second safety net.
	/// </remarks>
	/// <seealso cref="PawnInvisibilityCache"/>
	[HarmonyPatch(typeof(HediffComp_Invisibility))]
	[HarmonyPatch(nameof(HediffComp_Invisibility.CompPostPostRemoved))]
	internal static class Verse_HediffComp_Invisibility_CompPostPostRemoved
	{
		static bool Prepare()
		{
			return PrepatcherState.InvisibilityActive;
		}

		static void Postfix(HediffComp_Invisibility __instance)
		{
			var pawn = __instance.parent?.pawn;
			if (pawn != null)
				PawnInvisibilityCache.Rebuild(pawn);
		}
	}
}
