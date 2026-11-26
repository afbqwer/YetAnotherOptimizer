using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Serves <see cref="DrugPolicy"/>'s <see cref="ThingDef"/> indexer from
	/// <see cref="DrugPolicyIndexCache"/> instead of the vanilla linear scan.
	/// </summary>
	/// <remarks>
	/// Only a validated hit short-circuits the getter; every miss lets the original run, so a def that
	/// is not in the policy still throws <see cref="System.ArgumentException"/> as before.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptDrugPolicyCache"/>
	/// <seealso cref="DrugPolicyIndexCache"/>
	[HarmonyPatch]
	internal static class RimWorld_DrugPolicy_Item
	{
		// AccessTools.PropertyGetter has no argument-list overload in Harmony 3 and the two indexers
		// share the get_Item name, so the parameter type selects the ThingDef getter.
		private static readonly MethodBase Target = AccessTools.Method(typeof(DrugPolicy), "get_Item",
			new[] { typeof(ThingDef) });

		static MethodBase TargetMethod()
		{
			return Target;
		}

		static bool Prepare()
		{
			DrugPolicyIndexCache.Clear();
			return YaOptGlobal.Settings?.OptDrugPolicyCache.Enabled == true && Target != null;
		}

		// Run after foreign prefixes: when another mod short-circuits the getter its result must win,
		// otherwise the cache could replay a stale entry.
		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(DrugPolicy __instance, ThingDef drugDef, ref DrugPolicyEntry __result)
		{
			return !DrugPolicyIndexCache.TryGet(__instance, drugDef, out __result);
		}
	}
}