using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises <see cref="PawnApparelGenerator"/>'s material restriction check for the duration of
	/// one apparel generation call.
	/// </summary>
	/// <remarks>
	/// Apparel generation retries the candidate set up to 80 times and re-runs the same per-pair
	/// check on every attempt. Only pawns whose result can actually be expensive - specific apparel
	/// requirements or a faction stuff filter - are memoised. Disabled when another mod patches the
	/// method, because a replay would skip its code.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches"/>
	[HarmonyPatch(typeof(PawnApparelGenerator), "CanUseStuff")]
	internal static class RimWorld_PawnApparelGenerator_CanUseStuff
	{
		private const string MethodName = "CanUseStuff";

		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled != true || PawnGearRebuildCompat.Ready)
				return false;

			var target = AccessTools.Method(typeof(PawnApparelGenerator), MethodName);
			return !PatchConflictGuard.HasForeignPatch(target);
		}

		static bool Prefix(Pawn pawn, ThingStuffPair pair, ref bool __result)
		{
			if (!PawnGearSessionCaches.IsApparelStuffMemoUseful(pawn))
				return true;
			if (!PawnGearSessionCaches.TryGetApparelStuffUsable(pawn, pair, out var cached))
				return true;

			__result = cached;
			return false;
		}

		static void Postfix(Pawn pawn, ThingStuffPair pair, bool __result, bool __runOriginal)
		{
			if (!__runOriginal)
				return;
			if (!PawnGearSessionCaches.IsApparelStuffMemoUseful(pawn))
				return;

			PawnGearSessionCaches.StoreApparelStuffUsable(pawn, pair, __result);
		}
	}
}
