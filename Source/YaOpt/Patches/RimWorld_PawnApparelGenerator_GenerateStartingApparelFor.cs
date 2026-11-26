using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Opens and closes the apparel generation memo session around
	/// <see cref="PawnApparelGenerator.GenerateStartingApparelFor"/>.
	/// </summary>
	/// <remarks>
	/// The session is closed in a finalizer so every early return releases it. Humanoid Alien Races
	/// patches this method with a pool filter; the session only records the pawn, so both patches
	/// coexist.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches"/>
	[HarmonyPatch(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateStartingApparelFor))]
	internal static class RimWorld_PawnApparelGenerator_GenerateStartingApparelFor
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled == true;
		}

		static void Prefix(Pawn pawn, out bool __state)
		{
			__state = YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled == true
				&& PawnGearSessionCaches.BeginApparelSession(pawn);
		}

		static void Finalizer(bool __state)
		{
			if (__state)
				PawnGearSessionCaches.EndApparelSession();
		}
	}
}
