using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes terrain <see cref="StatDefOf.Beauty"/> and <see cref="StatDefOf.BeautyOutdoors"/>
	/// abstract lookups through <see cref="TerrainBeautyCache"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="BeautyUtility.CellBeauty"/> evaluates the terrain's abstract Beauty value for every
	/// sampled cell. Terrain defs are immutable during play, and an abstract request has no
	/// <see cref="Thing"/> for the vanilla <c>StatWorker</c> caches to key on, so the value is stored
	/// per <see cref="TerrainDef"/>. Only requests without a stuff def are cached; other abstract
	/// stats and non-terrain defs are left untouched.
	/// </remarks>
	/// <seealso cref="TerrainBeautyCache"/>
	/// <seealso cref="YaOptSettings.OptTerrainBeautyCache"/>
	[HarmonyPatch(typeof(StatWorker))]
	[HarmonyPatch(nameof(StatWorker.GetValueAbstract), typeof(BuildableDef), typeof(ThingDef))]
	internal static class RimWorld_StatWorker_GetValueAbstract_TerrainBeautyCache
	{
		static bool Prepare()
		{
			TerrainBeautyCache.Invalidate();
			return YaOptGlobal.Settings?.OptTerrainBeautyCache.Enabled == true;
		}

		static bool Prefix(StatDef ___stat, BuildableDef def, ThingDef stuffDef, ref float __result)
		{
			if (stuffDef != null || !(def is TerrainDef terrain))
				return true;
			if (___stat != StatDefOf.Beauty && ___stat != StatDefOf.BeautyOutdoors)
				return true;

			return !TerrainBeautyCache.TryGet(terrain, ___stat == StatDefOf.BeautyOutdoors, out __result);
		}

		static void Postfix(StatDef ___stat, BuildableDef def, ThingDef stuffDef, float __result, bool __runOriginal)
		{
			if (!__runOriginal || stuffDef != null || !(def is TerrainDef terrain))
				return;
			if (___stat != StatDefOf.Beauty && ___stat != StatDefOf.BeautyOutdoors)
				return;

			TerrainBeautyCache.Store(terrain, ___stat == StatDefOf.BeautyOutdoors, __result);
		}
	}
}