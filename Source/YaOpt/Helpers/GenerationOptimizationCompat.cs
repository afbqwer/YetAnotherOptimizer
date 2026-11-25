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
			foreach (var target in FastPathTargets())
			{
				if (target == null)
					continue;

				var conflict = PatchConflictGuard.FindForeignConflict(target);
				if (conflict == null)
					continue;

				Ban(nameof(YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath),
					"其他 Mod 补丁：" + conflict + " → " + target.DeclaringType?.Name + "." + target.Name);
				YaOptMod.Warning("OptThingSetMakerCanGenerateFastPath disabled: " + conflict + " patches " +
								 target.DeclaringType?.FullName + "." + target.Name + ".");
				return;
			}
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
			var conflict = PatchConflictGuard.FindForeignConflict(target, includeAll: true);
			if (conflict == null)
				return;

			Ban(nameof(YaOpt.YaOptSettings.OptThingSetMakerSampleCache),
				"其他 Mod 补丁：" + conflict + " → ThingSetMaker_MarketValue.GeneratePossibleDefs");
			YaOptMod.Warning("OptThingSetMakerSampleCache disabled: " + conflict +
							 " patches ThingSetMaker_MarketValue.GeneratePossibleDefs.");
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

		private static void Ban(string settingId, string reason)
		{
			if (!CompatibilityDefines.CachedBannedOptimizations.Add(settingId))
				return;

			CompatibilityDefines.CachedBannedBy[settingId] = reason;
		}
	}
}
