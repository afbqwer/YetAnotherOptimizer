using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Reuses toggle tab visibility checks within one frame to avoid repeating the same query.
	/// </summary>
	/// <remarks>
	/// Only the getter a worker type declares is answered from the cache, never an inherited one, and
	/// the answer is cached per worker instance rather than per type.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptToggleTabCheck"/>
	[HarmonyPatch]
	internal static class MultiTargets_ToggleTab
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var seen = new HashSet<MethodBase>();
			foreach (var type in ToggleTabCache.ToggleTabTypes)
			{
				var getter = AccessTools.DeclaredPropertyGetter(type, nameof(MainButtonWorker.Disabled));
				if (getter != null && seen.Add(getter))
					yield return getter;
			}
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptToggleTabCheck.Enabled && ToggleTabCache.ToggleTabTypes.Count > 0;
		}

		/// <remarks>
		/// Never writes <c>__result</c> on a miss. Another mod's prefix on the same getter may already
		/// have produced a value (and skipped the original); a blind write here would overwrite it with
		/// false and hand the game a tab that should stay disabled.
		/// </remarks>
		static bool Prefix(MainButtonWorker __instance, ref bool __result)
		{
			if (!ToggleTabCache.TryGetResult(__instance, out var cached))
				return true;
			__result = cached;
			return false;
		}

		static void Postfix(MainButtonWorker __instance, ref bool __result, bool __runOriginal)
		{
			if (__runOriginal)
			{
				ToggleTabCache.UpdateCache(__instance, __result);
			}
		}
	}
}
