using RimWorld;
using System.Runtime.CompilerServices;
using System.Threading;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	internal static class PawnShamblerCache
	{
		internal static bool IsShambler(Pawn pawn)
		{
			if (!ModsConfig.AnomalyActive)
				return false;
			if (pawn.mutant != null && pawn.mutant.Def == MutantDefOf.Shambler)
				return true;
			if (!PrepatcherState.ShamblerActive)
				return pawn.health.hediffSet.HasHediff(HediffDefOf.ShamblerCorpse);

			return Volatile.Read(ref PrepatcherFields.ShamblerState(pawn));
		}

		internal static void Rebuild(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null)
				return;

			var found = pawn.health.hediffSet.HasHediff(HediffDefOf.ShamblerCorpse);
			Volatile.Write(ref PrepatcherFields.ShamblerState(pawn), found);
		}
	}
}
