using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers Rosaline's birth-mother and birth-father lookups without LINQ iterators.
	/// </summary>
	/// <remarks>
	/// The original methods filter <see cref="Pawn_RelationsTracker.DirectRelations"/> by relation defName,
	/// skip entries whose other pawn is null and return the first match. The replacement keeps that order
	/// and identity check in one allocation-free loop. A patched Rosaline method disables the replacement.
	/// Mother and father are separate patch classes so neither prefix needs Harmony's
	/// <c>MethodBase __originalMethod</c> injection, which allocates a stub on every call.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	internal static class RosalineBirthParentLookup
	{
		internal static bool Resolve(Pawn pawn, string defName, ref Pawn result)
		{
			result = null;
			var relations = pawn?.relations?.DirectRelations;
			if (relations == null)
				return false;

			for (var i = 0; i < relations.Count; i++)
			{
				var relation = relations[i];
				if (relation.otherPawn != null && relation.def.defName == defName)
				{
					result = relation.otherPawn;
					return false;
				}
			}

			return false;
		}
	}

	[HarmonyPatch]
	internal static class MultiTargets_RosalineBirthMother
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var method = PawnGenerationIndexCompat.RosalineMother;
			if (method != null)
				yield return method;
		}

		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled && PawnGenerationIndexCompat.RosalineReady;
		}

		static bool Prefix(Pawn pawn, ref Pawn __result)
		{
			return RosalineBirthParentLookup.Resolve(pawn, "SEX_BirthMother", ref __result);
		}
	}

	[HarmonyPatch]
	internal static class MultiTargets_RosalineBirthFather
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var method = PawnGenerationIndexCompat.RosalineFather;
			if (method != null)
				yield return method;
		}

		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled && PawnGenerationIndexCompat.RosalineReady;
		}

		static bool Prefix(Pawn pawn, ref Pawn __result)
		{
			return RosalineBirthParentLookup.Resolve(pawn, "SEX_BirthFather", ref __result);
		}
	}
}
