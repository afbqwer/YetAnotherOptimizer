using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="BackstoryCategoryFilter.Matches(BackstoryDef)"/> with the allocation-free,
	/// bitmap-backed implementation in <see cref="BackstoryMatchCache"/>.
	/// </summary>
	/// <remarks>
	/// Filling a shuffled backstory slot calls this once per loaded backstory with the same filter, and the
	/// vanilla body allocates a capturing delegate per <c>Any(...)</c> call. The replacement keeps the exact
	/// predicate order. Postfixes on the vanilla method still run.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnBackstoryMatchCache"/>
	/// <seealso cref="BackstoryMatchCache"/>
	[HarmonyPatch(typeof(BackstoryCategoryFilter), nameof(BackstoryCategoryFilter.Matches),
		new[] { typeof(BackstoryDef) })]
	internal static class RimWorld_BackstoryCategoryFilter_Matches
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnBackstoryMatchCache.Enabled == true;
		}

		static bool Prefix(BackstoryCategoryFilter __instance, BackstoryDef backstory, ref bool __result)
		{
			__result = BackstoryMatchCache.Matches(__instance, backstory);
			return false;
		}
	}
}