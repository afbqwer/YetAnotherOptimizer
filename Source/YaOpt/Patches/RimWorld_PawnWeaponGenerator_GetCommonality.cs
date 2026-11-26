using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises <see cref="PawnWeaponGenerator"/>'s candidate weight for the duration of one weapon
	/// generation call.
	/// </summary>
	/// <remarks>
	/// <c>TryRandomElementByWeight</c> makes two passes over the candidate list, so every pair's
	/// weight - including the ideo disposition walk and the xenotype weapon-class check - is computed
	/// twice. The pawn and the pair are fixed within the call, so the second evaluation can reuse the
	/// first. Disabled when another mod patches the method, because a replay would skip its code.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches"/>
	[HarmonyPatch(typeof(PawnWeaponGenerator), "GetCommonality")]
	internal static class RimWorld_PawnWeaponGenerator_GetCommonality
	{
		private const string MethodName = "GetCommonality";

		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled != true || PawnGearRebuildCompat.Ready)
				return false;

			var target = AccessTools.Method(typeof(PawnWeaponGenerator), MethodName);
			return !PatchConflictGuard.HasForeignPatch(target);
		}

		static bool Prefix(Pawn pawn, ThingStuffPair pair, ref float __result)
		{
			if (!PawnGearSessionCaches.TryGetWeaponCommonality(pawn, pair, out var cached))
				return true;

			__result = cached;
			return false;
		}

		static void Postfix(Pawn pawn, ThingStuffPair pair, float __result, bool __runOriginal)
		{
			if (__runOriginal)
				PawnGearSessionCaches.StoreWeaponCommonality(pawn, pair, __result);
		}
	}
}
