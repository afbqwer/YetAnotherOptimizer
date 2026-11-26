using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Opens and closes the gene notification batch around <see cref="PawnGenerator.GenerateGenes"/>.
	/// </summary>
	/// <remarks>
	/// The finalizer flushes the deferred work on every exit path. Adding genes outside generation keeps
	/// the original per-gene notification path because the batch is not open there.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="GeneNotificationBatch"/>
	[HarmonyPatch(typeof(PawnGenerator), "GenerateGenes")]
	internal static class Verse_PawnGenerator_GenerateGenes
	{
		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled && PawnGenerationIndexCompat.GeneBatchReady;
		}

		static void Prefix(Pawn pawn, out bool __state)
		{
			__state = PawnGenerationIndexCompat.MasterEnabled
				&& PawnGenerationIndexCompat.GeneBatchReady
				&& GeneNotificationBatch.Begin(pawn);
		}

		static void Finalizer(bool __state)
		{
			GeneNotificationBatch.End(__state);
		}
	}
}