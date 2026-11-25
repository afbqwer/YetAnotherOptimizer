using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Defines;
using YaOpt.Settings;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Shared guards and startup conflict checks for the def lookup indexes and the third-party
	/// startup accelerations.
	/// </summary>
	internal static class DefLookupCacheHelper
	{
		/// <summary>
		/// Another def-index provider; running both would install duplicate patches on the same
		/// lookups and serve two competing caches.
		/// </summary>
		internal const string DuplicateProviderModId = "assssssqwww.allrecipescache";

		internal static bool CanUse(OptimizationOption option)
		{
			return option.Enabled && !YaOptGlobal.HasMod(DuplicateProviderModId);
		}

		/// <summary>
		/// Builds every enabled index. Called once per load, before the first
		/// <c>[StaticConstructorOnStartup]</c> constructor runs.
		/// </summary>
		internal static void BuildAll(string source)
		{
			CheckConflicts();

			if (YaOptGlobal.Settings.OptDefLookupCache.Enabled)
				DefLookupIndex.Build(source);

			if (YaOptGlobal.Settings.OptBiomeAnimalIndex.Enabled)
				BiomeAnimalIndex.Build();
		}

		/// <summary>
		/// Disables the affected options when the indexed lookups are already patched by another
		/// mod, or when a second def-index provider is active. Returns true when a new ban was
		/// added.
		/// </summary>
		internal static bool CheckConflicts()
		{
			var changed = false;

			if (YaOptGlobal.HasMod(DuplicateProviderModId))
			{
				changed |= Ban(nameof(YaOptSettings.OptDefLookupCache), "重复的 Def 查找缓存 Mod");
				changed |= Ban(nameof(YaOptSettings.OptBiomeAnimalIndex), "重复的 Def 查找缓存 Mod");
				changed |= Ban(nameof(YaOptSettings.OptGiddyUpOffsetCache), "重复的 Def 查找缓存 Mod");
				changed |= Ban(nameof(YaOptSettings.OptExosuitXmlFast), "重复的 Def 查找缓存 Mod");
				return changed;
			}

			var recipeTargetsPatched =
				HasForeignPatch(AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.AllRecipes))) ||
				HasForeignPatch(AccessTools.PropertyGetter(typeof(RecipeDef), nameof(RecipeDef.AllRecipeUsers))) ||
				HasForeignPatch(AccessTools.PropertyGetter(typeof(RecipeDef), nameof(RecipeDef.IsSurgery))) ||
				HasForeignPatch(AccessTools.PropertyGetter(typeof(ResearchProjectDef),
					nameof(ResearchProjectDef.UnlockedDefs))) ||
				HasForeignPatch(AccessTools.Method(typeof(ResearchPrerequisitesUtility),
					nameof(ResearchPrerequisitesUtility.ComputeResearchPrerequisites))) ||
				HasForeignPatch(AccessTools.Method(typeof(ResearchPrerequisitesUtility),
					nameof(ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites)));
			if (recipeTargetsPatched)
				changed |= Ban(nameof(YaOptSettings.OptDefLookupCache), "其他配方或研究索引补丁");

			var biomeTargetsPatched =
				HasForeignPatch(AccessTools.PropertyGetter(typeof(BiomeDef), nameof(BiomeDef.AllWildAnimals))) ||
				HasForeignPatch(AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfAnimal))) ||
				HasForeignPatch(AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfPollutionAnimal))) ||
				HasForeignPatch(AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfCoastalAnimal)));
			if (biomeTargetsPatched)
				changed |= Ban(nameof(YaOptSettings.OptBiomeAnimalIndex), "其他生物群系动物索引补丁");

			// The GiddyUp patch only seeds and saves around the original pass, so it stays compatible
			// with other patches on ProcessPawnKinds. The Exosuit prefix replaces the original
			// implementation, so a foreign patch there would be skipped.
			var exosuitWorker = AccessTools.Method(AccessTools.TypeByName("Exosuit.XmlBackCompat"), "ApplyWorker");
			if (HasForeignPatch(exosuitWorker))
				changed |= Ban(nameof(YaOptSettings.OptExosuitXmlFast), "其他 Exosuit 补丁");

			// A ban added from XML (CompatibilityDef) or a disabled option must also drop data that
			// was built earlier in the load.
			if (!YaOptGlobal.Settings.OptDefLookupCache.Enabled)
				DefLookupIndex.Invalidate();

			if (!YaOptGlobal.Settings.OptBiomeAnimalIndex.Enabled)
				BiomeAnimalIndex.Invalidate();

			return changed;
		}

		private static bool Ban(string settingId, string reason)
		{
			if (!CompatibilityDefines.CachedBannedOptimizations.Add(settingId))
				return false;

			CompatibilityDefines.CachedBannedBy[settingId] = reason;
			YaOptMod.Debug($"{settingId} disabled: {reason}");

			// Drop the built data so the getter patches immediately fall back to vanilla.
			if (settingId == nameof(YaOptSettings.OptDefLookupCache))
				DefLookupIndex.Invalidate();
			else if (settingId == nameof(YaOptSettings.OptBiomeAnimalIndex))
				BiomeAnimalIndex.Invalidate();

			return true;
		}

		private static bool HasForeignPatch(MethodBase target)
		{
			if (target == null)
				return false;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return false;

			var harmony = YaOptMod.Instance.Harmony;
			var earlyHarmony = YaOptMod.Instance.EarlyHarmony;
			foreach (var owner in info.Owners)
			{
				if (owner != harmony.Id && owner != earlyHarmony.Id)
					return true;
			}

			return false;
		}
	}
}