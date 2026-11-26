using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Defines;
using YaOpt.Patches;
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
			return option != null && option.Enabled && !YaOptGlobal.HasMod(DuplicateProviderModId);
		}

		/// <summary>
		/// Builds every enabled index. Called once per load, before the first
		/// <c>[StaticConstructorOnStartup]</c> constructor runs.
		/// </summary>
		internal static void BuildAll(string source)
		{
			CheckConflicts();

			if (CanUse(YaOptGlobal.Settings.OptDefLookupCache))
				DefLookupIndex.Build(source);

			if (CanUse(YaOptGlobal.Settings.OptBiomeAnimalIndex))
				BiomeAnimalIndex.Build();
		}

		internal static void InvalidateAll()
		{
			DefLookupIndex.Invalidate();
			BiomeAnimalIndex.Invalidate();
		}

		internal static void QueueRebuild(string source)
		{
			LongEventHandler.QueueLongEvent(() =>
			{
				if (!PlayDataLoader.Loaded)
					return;

				InvalidateAll();
				BuildAll(source);
			}, "YaOpt.Loading.Patching".Translate(), false, null, false);
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
				changed |= HardBan(nameof(YaOptSettings.OptDefLookupCache), "重复的 Def 查找缓存 Mod");
				changed |= HardBan(nameof(YaOptSettings.OptBiomeAnimalIndex), "重复的 Def 查找缓存 Mod");
				changed |= HardBan(nameof(YaOptSettings.OptRecipeUsersCache), "重复的 Def 查找缓存 Mod");
				changed |= HardBan(nameof(YaOptSettings.OptGiddyUpOffsetCache), "重复的 Def 查找缓存 Mod");
				changed |= HardBan(nameof(YaOptSettings.OptExosuitXmlFast), "重复的 Def 查找缓存 Mod");
				InvalidateAll();
				return changed;
			}

			var reasons = new List<string>();
			CollectCompatible(reasons, IsCompatibleDefLookupPatch,
				AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.AllRecipes)),
				AccessTools.PropertyGetter(typeof(RecipeDef), nameof(RecipeDef.AllRecipeUsers)),
				AccessTools.PropertyGetter(typeof(RecipeDef), nameof(RecipeDef.IsSurgery)),
				AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.UnlockedDefs)));
			if (reasons.Count > 0)
				changed |= DefaultOff(nameof(YaOptSettings.OptDefLookupCache), reasons);

			reasons = new List<string>();
			Collect(reasons, AccessTools.PropertyGetter(typeof(RecipeDef), nameof(RecipeDef.AllRecipeUsers)));
			if (reasons.Count > 0)
				changed |= DefaultOff(nameof(YaOptSettings.OptRecipeUsersCache), reasons);

			reasons = new List<string>();
			CollectCompatible(reasons, IsCompatibleBiomePatch,
				AccessTools.PropertyGetter(typeof(BiomeDef), nameof(BiomeDef.AllWildAnimals)),
				AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfAnimal)),
				AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfPollutionAnimal)),
				AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfCoastalAnimal)));
			if (reasons.Count > 0)
				changed |= DefaultOff(nameof(YaOptSettings.OptBiomeAnimalIndex), reasons);

			// The GiddyUp patch only seeds and saves around the original pass, so it stays compatible
			// with other patches on ProcessPawnKinds. The Exosuit prefix replaces the original
			// implementation, so a foreign patch there would be skipped.
			var exosuitWorker = AccessTools.Method(AccessTools.TypeByName("Exosuit.XmlBackCompat"), "ApplyWorker");
			var exosuitReasons = new List<string>();
			Collect(exosuitReasons, exosuitWorker);
			if (exosuitReasons.Count > 0)
				changed |= DefaultOff(nameof(YaOptSettings.OptExosuitXmlFast), exosuitReasons);

			// A ban added from XML (CompatibilityDef) or a disabled option must also drop data that
			// was built earlier in the load.
			if (!YaOptGlobal.Settings.OptDefLookupCache.Enabled)
				DefLookupIndex.Invalidate();

			if (!YaOptGlobal.Settings.OptBiomeAnimalIndex.Enabled)
				BiomeAnimalIndex.Invalidate();

			if (!YaOptGlobal.Settings.OptRecipeUsersCache.Enabled)
				Verse_RecipeDef_AllRecipeUsers.ClearCache();

			return changed;
		}

		/// <summary>
		/// A duplicate def-index provider is a known mod conflict, so the option is locked off.
		/// </summary>
		private static bool HardBan(string settingId, string reason)
		{
			if (CompatibilityDefines.CachedBannedOptimizations.Contains(settingId))
				return false;

			CompatibilityDefines.Ban(settingId, reason);
			YaOptMod.Debug($"{settingId} banned: {reason}");

			// Drop the built data so the getter patches immediately fall back to vanilla.
			InvalidateData(settingId);
			return true;
		}

		/// <summary>
		/// A foreign patch disables the option by default, but the player may enable it. Data is
		/// dropped by the enabled checks that follow the detection pass.
		/// </summary>
		private static bool DefaultOff(string settingId, IReadOnlyList<string> reasons)
		{
			var added = CompatibilityDefines.PatchConflict(settingId, reasons);
			YaOptMod.Debug($"{settingId} disabled by default: {string.Join(", ", reasons)}");
			return added;
		}

		private static void InvalidateData(string settingId)
		{
			if (settingId == nameof(YaOptSettings.OptDefLookupCache))
				DefLookupIndex.Invalidate();
			else if (settingId == nameof(YaOptSettings.OptBiomeAnimalIndex))
				BiomeAnimalIndex.Invalidate();
			else if (settingId == nameof(YaOptSettings.OptRecipeUsersCache))
				Verse_RecipeDef_AllRecipeUsers.ClearCache();
		}

		private static bool IsCompatibleDefLookupPatch(MethodBase target, Patch patch)
		{
			var method = patch.PatchMethod;
			if (target == null || method == null)
				return false;

			if (target == AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.AllRecipes)))
			{
				return IsPatch(patch, "XeoNovaDan.CarpenterTable", "ThingDef_AllRecipes_Getter", "Prefix") ||
					   IsPatch(patch, "XeoNovaDan.CarpenterTable", "ThingDef_AllRecipes_Getter", "Postfix") ||
					   IsPatch(patch, "zal.xenobionicpatcher", "HarmonyPatches", "AllRecipes_Postfix");
			}

			if (target != AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.UnlockedDefs)))
				return false;

			return IsPatch(patch, "com.Axolotl.rimworld.mod", "MoeLotlResearchProjectDefPatch", "Postfix") ||
				   IsPatch(patch, "DMSE", "Patch_ResearchProjectDef_UnlockedDefs", "Postfix") ||
				   IsPatch(patch, "Kikohi.PipeSystem", "ResearchProjectDef_UnlockedDefs_Patch", "Postfix") ||
				   IsPatch(patch, "OskarPotocki.VEF",
					   "VanillaExpandedFramework_ResearchProjectDef_UnlockedDefs_Patch", "Postfix") ||
				   IsPatch(patch, "com.VQETheGenerator",
					   "VanillaQuestsExpandedTheGenerator_ResearchProjectDef_UnlockedDefs_Patch", "Postfix");
		}

		private static bool IsPatch(Patch patch, string owner, string typeName, string methodName)
		{
			return patch.owner == owner && patch.PatchMethod?.DeclaringType?.Name == typeName &&
				   patch.PatchMethod.Name == methodName;
		}

		private static bool IsCompatibleBiomePatch(MethodBase target, Patch patch)
		{
			return target == AccessTools.Method(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfAnimal)) &&
				   IsPatch(patch, "com.alphaanimals", "AlphaAnimals_BiomeDef_CommonalityOfAnimal_Patch",
					   "MultiplyAlphaAnimalCommonality");
		}

		private static void CollectCompatible(List<string> results, Func<MethodBase, Patch, bool> compatiblePatch,
			params MethodBase[] targets)
		{
			for (var i = 0; i < targets.Length; i++)
				PatchConflictGuard.CollectForeignConflicts(targets[i], true, results, compatiblePatch);
		}

		/// <summary>
		/// Collects every foreign patch on the targets, including postfixes, matching the old
		/// has-any-patch check.
		/// </summary>
		private static void Collect(List<string> results, params MethodBase[] targets)
		{
			for (var i = 0; i < targets.Length; i++)
				PatchConflictGuard.CollectForeignConflicts(targets[i], includeAll: true, results);
		}
	}
}