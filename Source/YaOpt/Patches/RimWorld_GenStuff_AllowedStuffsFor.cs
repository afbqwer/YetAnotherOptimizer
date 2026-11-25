using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the <see cref="GenStuff.AllowedStuffsFor"/> iterator with a cached per-def list.
	/// </summary>
	/// <remarks>
	/// Item generation asks for the same def's allowed materials up to five times per def per sample, and
	/// each vanilla call walks every stuff def. The list only depends on def data. Disabled when another
	/// patch targets the method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptGenerationDefCache"/>
	/// <seealso cref="GenerationDefCaches"/>
	[HarmonyPatch(typeof(GenStuff), nameof(GenStuff.AllowedStuffsFor))]
	internal static class RimWorld_GenStuff_AllowedStuffsFor
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptGenerationDefCache.Enabled != true)
				return false;

			var target = AccessTools.Method(typeof(GenStuff), nameof(GenStuff.AllowedStuffsFor));
			return !PatchConflictGuard.HasForeignConflict(target);
		}

		static bool Prefix(BuildableDef td, TechLevel maxTechLevel, bool checkAllowedInStuffGeneration,
			ref IEnumerable<ThingDef> __result)
		{
			if (td == null)
				return true;

			__result = GenerationDefCaches.BuildAllowedStuffs(td, maxTechLevel, checkAllowedInStuffGeneration);
			return false;
		}
	}
}
