using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches <see cref="PawnWeaponGenerator.IsDerpWeapon"/> for the material combinations that actually
	/// evaluate a stat.
	/// </summary>
	/// <remarks>
	/// Only melee weapons with a material reach <c>stuff.GetStatValueAbstract(multStat)</c>; every other
	/// combination is two field reads and is left untouched so the common path pays nothing. The result
	/// depends only on the two defs. Disabled when another patch targets the method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptGenerationDefCache"/>
	/// <seealso cref="GenerationDefCaches"/>
	[HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.IsDerpWeapon))]
	internal static class RimWorld_PawnWeaponGenerator_IsDerpWeapon
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptGenerationDefCache.Enabled != true)
				return false;

			return AccessTools.Method(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.IsDerpWeapon)) != null;
		}

		static bool Prefix(ThingDef thing, ThingDef stuff, ref bool __result)
		{
			if (stuff == null || thing == null || !thing.IsMeleeWeapon)
				return true;

			if (GenerationDefCaches.TryGetDerpWeapon(thing, stuff, out __result))
				return false;

			return true;
		}

		static void Postfix(ThingDef thing, ThingDef stuff, bool __result, bool __runOriginal)
		{
			if (!__runOriginal || stuff == null || thing == null || !thing.IsMeleeWeapon)
				return;

			GenerationDefCaches.StoreDerpWeapon(thing, stuff, __result);
		}
	}
}
