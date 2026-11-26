using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Compatibility.HumanoidAlienRaces
{
	/// <summary>
	/// Replaces Humanoid Alien Races' lazy trait validator with the materialised candidate list.
	/// </summary>
	/// <remarks>
	/// HAR's validator returns <c>traits.Where(tr =&gt; CanGetTrait(tr, pawn))</c>. The weighted sampler
	/// enumerates its source twice and each draw re-runs the whole race check, which itself allocates the
	/// pawn's backstory restriction list and rolls HAR chances. Returning a prebuilt list makes the sampler
	/// take its <see cref="System.Collections.IList"/> path and validates every trait once per pawn.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnTraitsHarFastPath"/>
	/// <seealso cref="HarTraitCandidates"/>
	[HarmonyPatch]
	internal static class AlienRace_GenerateTraitsValidator
	{
		static MethodBase TargetMethod()
		{
			return HarTraitCandidates.Validator;
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnTraitsHarFastPath.Enabled == true
				&& HarTraitCandidates.Ready;
		}

		static bool Prefix(List<TraitDef> __0, Pawn __1, ref IEnumerable<TraitDef> __result)
		{
			var candidates = HarTraitCandidates.Get(__1, __0);
			if (candidates == null)
				return true;

			__result = candidates;
			return false;
		}
	}
}