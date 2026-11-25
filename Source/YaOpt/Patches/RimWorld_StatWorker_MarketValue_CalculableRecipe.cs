using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches the recipe lookup used to price a def.
	/// </summary>
	/// <remarks>
	/// When a def has no explicit market value and no cost list, vanilla scans every recipe def on each
	/// evaluation. The answer depends only on def data. Disabled when another patch targets the method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptGenerationDefCache"/>
	/// <seealso cref="GenerationDefCaches"/>
	[HarmonyPatch(typeof(StatWorker_MarketValue), nameof(StatWorker_MarketValue.CalculableRecipe))]
	internal static class RimWorld_StatWorker_MarketValue_CalculableRecipe
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptGenerationDefCache.Enabled != true)
				return false;

			var target = AccessTools.Method(typeof(StatWorker_MarketValue), nameof(StatWorker_MarketValue.CalculableRecipe));
			return !PatchConflictGuard.HasForeignConflict(target);
		}

		static bool Prefix(BuildableDef def, ref RecipeDef __result)
		{
			if (def == null)
				return true;

			if (GenerationDefCaches.TryGetCalculableRecipe(def, out __result))
				return false;

			return true;
		}

		static void Postfix(BuildableDef def, RecipeDef __result, bool __runOriginal)
		{
			if (!__runOriginal || def == null)
				return;

			GenerationDefCaches.StoreCalculableRecipe(def, __result);
		}
	}
}
