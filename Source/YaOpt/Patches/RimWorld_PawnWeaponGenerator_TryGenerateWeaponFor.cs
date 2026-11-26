using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Opens and closes the weapon generation memo session around
	/// <see cref="PawnWeaponGenerator.TryGenerateWeaponFor"/>.
	/// </summary>
	/// <remarks>
	/// The session is closed in a finalizer so early returns also release it. Humanoid Alien Races
	/// patches this method with a pool filter; the session only records the pawn and does not depend
	/// on that pool, so both patches coexist.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches"/>
	[HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.TryGenerateWeaponFor))]
	internal static class RimWorld_PawnWeaponGenerator_TryGenerateWeaponFor
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled == true;
		}

		static void Prefix(Pawn pawn, out bool __state)
		{
			__state = YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled == true
				&& PawnGearSessionCaches.BeginWeaponSession(pawn);
		}

		static void Finalizer(bool __state)
		{
			if (__state)
				PawnGearSessionCaches.EndWeaponSession();
		}
	}
}
