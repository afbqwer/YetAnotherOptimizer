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
	/// Runs at the highest prefix priority so HAR's pool-filtering prefix, which is neutralised anyway,
	/// never executes before the replacement body. On a runtime failure the rewrite disables itself; this
	/// prefix then lets the original method and HAR's own prefixes run again.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="PawnGearRebuildCompat"/>
	[HarmonyPatch(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateStartingApparelFor))]
	[HarmonyPriority(Priority.First)]
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