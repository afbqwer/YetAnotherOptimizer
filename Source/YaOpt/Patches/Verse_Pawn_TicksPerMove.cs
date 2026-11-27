using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises <see cref="Pawn.TicksPerMove"/> for the current game tick.
	/// </summary>
	/// <remarks>
	/// Both path parameterization and <c>Pawn_PathFollower.CostToMoveIntoCell</c> call this per cell, so a
	/// pawn is re-evaluated dozens of times per tick with inputs that only change between ticks. The cache
	/// is one tick wide, so it cannot mask a mid-walk stat change beyond a single tick.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathMoveTicksCache"/>
	[HarmonyPatch(typeof(Pawn))]
	[HarmonyPatch(nameof(Pawn.TicksPerMove))]
	internal static class Verse_Pawn_TicksPerMove
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathMoveTicksCache.Enabled;
		}

		static bool Prefix(Pawn __instance, bool diagonal, ref float __result)
		{
			return !TicksPerMoveCache.TryGet(__instance, diagonal, out __result);
		}

		static void Postfix(Pawn __instance, bool diagonal, float __result)
		{
			TicksPerMoveCache.Store(__instance, diagonal, __result);
		}
	}
}