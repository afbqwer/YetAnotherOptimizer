using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Answers <see cref="PawnApparelGenerator"/>'s candidate check from the per-definition cache in
	/// <see cref="PawnApparelPairCache"/>.
	/// </summary>
	/// <remarks>
	/// The vanilla method is called once per apparel pair, so the same headgear, wearable, tag and
	/// generated-chance checks run thousands of times for one pawn. The cache key is the pawn plus the
	/// budget, headgear flag and fixed seed, so a different pawn or parameter set falls back to the vanilla
	/// body. The price check is per pair and remains in the prefix.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnApparelPairCache"/>
	/// <seealso cref="PawnApparelPairCache"/>
	[HarmonyPatch(typeof(PawnApparelGenerator), "CanUsePair")]
	internal static class RimWorld_PawnApparelGenerator_CanUsePair
	{
		private const string MethodName = "CanUsePair";

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnApparelPairCache.Enabled == true
				&& AccessTools.Method(typeof(PawnApparelGenerator), MethodName) != null;
		}

		static bool Prefix(ThingStuffPair pair, Pawn pawn, float moneyLeft, bool allowHeadgear,
			int fixedSeed, ref bool __result)
		{
			return !PawnApparelPairCache.TryAnswer(pair, pawn, moneyLeft, allowHeadgear, fixedSeed,
				out __result);
		}
	}
}