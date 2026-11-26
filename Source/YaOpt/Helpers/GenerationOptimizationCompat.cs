using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Disables generation optimizations when another mod changes the sampling code they mirror, cache or
	/// bypass.
	/// </summary>
	/// <remarks>
	/// The shortcut reproduces the first iteration of <c>GenerateDefsWithPossibleTotalValue</c> and its
	/// private helpers, and no longer calls that method from <c>CanGenerateSub</c>. A third-party
	/// transpiler or boolean prefix on any of those members would make our answer disagree with the real
	/// generation, so the option is disabled instead. The sample cache replays a skipped
	/// <c>GeneratePossibleDefs</c> call, so any foreign patch on it, postfixes included, would be applied
	/// twice and disables that option too. The check runs on every <see cref="Patches.Patcher.TryPatch"/>
	/// before patches are applied, so the setting already reports as disabled.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath"/>
	/// <seealso cref="YaOpt.YaOptSettings.OptThingSetMakerSampleCache"/>
	internal static class GenerationOptimizationCompat
	{
		internal static void Refresh()
		{
			CheckCanGenerateFastPath();
			CheckSampleCache();
		}

		private static void CheckCanGenerateFastPath()
		{
			var reasons = new List<string>();
			foreach (var target in FastPathTargets())
			{
				if (target == null)
					continue;

				PatchConflictGuard.CollectForeignConflicts(target, includeAll: false, reasons);
			}

			if (reasons.Count == 0)
				return;

			DefaultOff(nameof(YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath), reasons);
			YaOptMod.Warning("OptThingSetMakerCanGenerateFastPath disabled by default: " +
							 string.Join(", ", reasons));
		}

		private static void CheckSampleCache()
		{
			var target = AccessTools.Method(typeof(ThingSetMaker_MarketValue),
				nameof(ThingSetMaker_MarketValue.GeneratePossibleDefs),
				new[] { typeof(ThingSetMakerParams), typeof(float).MakeByRefType(), typeof(int) });
			if (target == null)
				return;

			// The cached list already contains the effects of any postfix, so replaying the cache would
			// apply them a second time.
			var reasons = new List<string>();
			PatchConflictGuard.CollectForeignConflicts(target, includeAll: true, reasons);
			if (reasons.Count == 0)
				return;

			DefaultOff(nameof(YaOpt.YaOptSettings.OptThingSetMakerSampleCache), reasons);
			YaOptMod.Warning("OptThingSetMakerSampleCache disabled by default: " +
							 string.Join(", ", reasons));
		}

		private static MethodInfo[] FastPathTargets()
		{
			return new[]
			{
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), "CanGenerateSub"),
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), "AllowedThingDefs"),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GenerateDefsWithPossibleTotalValue)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.CalculateAllowedThingStuffPairs)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetTrashThreshold)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetNonTrashMass)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetMaxValueWithMaxMass)),
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), nameof(ThingSetMaker_MarketValue.GeneratePossibleDefs),
					new[] { typeof(ThingSetMakerParams), typeof(float).MakeByRefType() }),
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), nameof(ThingSetMaker_MarketValue.GeneratePossibleDefs),
					new[] { typeof(ThingSetMakerParams), typeof(float).MakeByRefType(), typeof(int) })
			};
		}

		private static void DefaultOff(string settingId, IReadOnlyList<string> reasons)
		{
			CompatibilityDefines.PatchConflict(settingId, reasons);
		}
	}
}
