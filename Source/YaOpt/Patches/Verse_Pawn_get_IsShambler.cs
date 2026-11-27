using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsShambler), MethodType.Getter)]
	internal static class Verse_Pawn_get_IsShambler
	{
		static bool Prepare()
		{
			return PrepatcherState.ShamblerActive;
		}

		static bool Prefix(Pawn __instance, ref bool __result)
		{
			__result = PawnShamblerCache.IsShambler(__instance);
			return false;
		}
	}
}
