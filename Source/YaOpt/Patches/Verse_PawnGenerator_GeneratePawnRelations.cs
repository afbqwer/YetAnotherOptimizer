using System;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="PawnGenerator.GeneratePawnRelations"/> with the single-pass weight sampler in
	/// <see cref="PawnRelationIndex"/>.
	/// </summary>
	/// <remarks>
	/// All prefixes and postfixes on the original method still run. Humanoid Alien Races' genderless
	/// relation prefix keeps ownership for the races it implements. A runtime failure disables the
	/// sampler and leaves the current call on the vanilla side.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	[HarmonyPatch(typeof(PawnGenerator), "GeneratePawnRelations")]
	internal static class Verse_PawnGenerator_GeneratePawnRelations
	{
		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled
				&& PawnGenerationIndexCompat.RelationReady;
		}

		static bool Prefix(Pawn pawn, ref PawnGenerationRequest request)
		{
			if (!PawnRelationIndex.CanReplace(pawn))
				return true;

			try
			{
				PawnRelationIndex.Generate(pawn, ref request);
				return false;
			}
			catch (Exception ex)
			{
				PawnGenerationIndexCompat.DisableRelations(ex);
				return false;
			}
		}
	}
}