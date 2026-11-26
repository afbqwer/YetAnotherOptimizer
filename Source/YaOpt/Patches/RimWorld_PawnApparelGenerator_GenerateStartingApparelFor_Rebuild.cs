using System;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PawnApparelGenerator.GenerateStartingApparelFor"/> with the constructive
	/// selection in <see cref="PawnGearRebuild"/>.
	/// </summary>
	/// <remarks>
	/// Runs before Passer Race's generation context so its HAR apparel restriction scope remains active.
	/// The rewrite skips the original body while postfixes and Passer's finalizer still run.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="PawnGearRebuildCompat"/>
	[HarmonyPatch(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateStartingApparelFor))]
	[HarmonyPriority(Priority.First)]
	[HarmonyBefore(PawnGearRebuildCompat.PasserHarmonyId)]
	internal static class RimWorld_PawnApparelGenerator_GenerateStartingApparelFor_Rebuild
	{
		static bool Prepare()
		{
			return PawnGearRebuildCompat.Ready;
		}

		static bool Prefix(Pawn pawn, PawnGenerationRequest request)
		{
			if (!PawnGearRebuildCompat.Active)
				return true;

			try
			{
				PawnGearRebuild.RebuildApparel(pawn, request);
				return false;
			}
			catch (Exception ex)
			{
				PawnGearRebuildCompat.Fail(ex);
				return true;
			}
		}
	}
}