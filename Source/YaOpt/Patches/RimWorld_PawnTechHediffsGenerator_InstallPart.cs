using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips tech hediff installation for a null def.
	/// </summary>
	/// <remarks>
	/// A kind def with a null entry in <c>techHediffsRequired</c> would otherwise flow into the
	/// recipe lookup and implant fallback. The null-safe market-value read in
	/// <see cref="TechHediffFallback.MarketValue"/> keeps the budget untouched, and this prefix
	/// stops the install itself.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationFailureGuard"/>
	/// <seealso cref="RimWorld_PawnTechHediffsGenerator_GenerateTechHediffsFor"/>
	[HarmonyPatch(typeof(PawnTechHediffsGenerator), "InstallPart")]
	internal static class RimWorld_PawnTechHediffsGenerator_InstallPart
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGenerationFailureGuard.Enabled == true
				&& AccessTools.Method(typeof(PawnTechHediffsGenerator), "InstallPart",
					new[] { typeof(Pawn), typeof(ThingDef) }) != null;
		}

		static bool Prefix(ThingDef __1)
		{
			return __1 != null;
		}
	}
}