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
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	[HarmonyPatch]
	internal static class MultiTargets_RosalineBirthParents
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var mother = PawnGenerationIndexCompat.RosalineMother;
			var father = PawnGenerationIndexCompat.RosalineFather;
			if (mother != null)
				yield return mother;
			if (father != null)
				yield return father;
		}

		static bool Prepare()
		{
			return PawnGenerationIndexCompat.MasterEnabled && PawnGenerationIndexCompat.RosalineReady;
		}

		static bool Prefix(Pawn pawn, ref Pawn __result, MethodBase __originalMethod)
		{
			if (!PawnGenerationIndexCompat.MasterEnabled || !PawnGenerationIndexCompat.RosalineReady)
				return true;

			__result = null;
			var relations = pawn?.relations?.DirectRelations;
			if (relations == null)
				return false;

			var defName = __originalMethod.Name == "GetBirthMother"
				? "SEX_BirthMother"
				: "SEX_BirthFather";
			for (var i = 0; i < relations.Count; i++)
			{
				var relation = relations[i];
				if (relation.otherPawn != null && relation.def.defName == defName)
				{
					__result = relation.otherPawn;
					return false;
				}
			}

			return false;
		}
	}
}