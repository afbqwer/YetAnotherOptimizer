using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Disables the first-iteration CanGenerate shortcut when another mod changes the sampling algorithm
	/// it mirrors or the methods it bypasses.
	/// </summary>
	/// <remarks>
	/// The shortcut reproduces the first iteration of <c>GenerateDefsWithPossibleTotalValue</c> and its
	/// private helpers, and no longer calls that method from <c>CanGenerateSub</c>. A third-party
	/// transpiler or boolean prefix on any of those members would make our answer disagree with the real
	/// generation, so the option is disabled instead. The check runs on every
	/// <see cref="Patches.Patcher.TryPatch"/> before patches are applied, so the setting already reports
	/// as disabled.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptThingSetMakerCanGenerateFastPath"/>
	internal static class GenerationOptimizationCompat
	{
		internal static void Refresh()
		{
			foreach (var target in Targets())
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

		private static MethodInfo[] Targets()
		{
			return new[]
			{
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), "CanGenerateSub"),
				AccessTools.Method(typeof(ThingSetMaker_MarketValue), "AllowedThingDefs"),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GenerateDefsWithPossibleTotalValue)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.CalculateAllowedThingStuffPairs)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetTrashThreshold)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetNonTrashMass)),
				AccessTools.Method(typeof(ThingSetMakerByTotalStatUtility), nameof(ThingSetMakerByTotalStatUtility.GetMaxValueWithMaxMass))
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
