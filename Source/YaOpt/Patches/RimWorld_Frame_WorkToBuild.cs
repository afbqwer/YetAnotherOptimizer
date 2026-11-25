using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises Frame.WorkToBuild by buildable def and stuff using the base-stat cache pattern.
	/// </summary>
	/// <remarks>
	/// Flat arrays hold the common null-stuff values. Stuff variants are stored in lazy per-def
	/// dictionaries so the cache does not allocate a dense BuildableDef × ThingDef table.
	/// </remarks>
	internal static class FrameWorkToBuildCache
	{
		private static float[] _thingWithoutStuff;
		private static float[] _terrainWithoutStuff;
		private static Dictionary<ThingDef, float>[] _thingWithStuff;

		static FrameWorkToBuildCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static void Invalidate()
		{
			_thingWithoutStuff = null;
			_terrainWithoutStuff = null;
			_thingWithStuff = null;
		}

		private static bool EnsureArrays()
		{
			// Def generation rewrites stat bases before a game exists; never freeze those intermediate values.
			if (Current.Game == null)
				return false;

			var thingCount = DefDatabase<ThingDef>.DefCount;
			var terrainCount = DefDatabase<TerrainDef>.DefCount;
			if (_thingWithoutStuff != null && _thingWithoutStuff.Length == thingCount
				&& _terrainWithoutStuff != null && _terrainWithoutStuff.Length == terrainCount
				&& _thingWithStuff != null && _thingWithStuff.Length == thingCount)
			{
				return true;
			}

			var thingValues = new float[thingCount];
			var terrainValues = new float[terrainCount];
			Array.Fill(thingValues, float.NaN);
			Array.Fill(terrainValues, float.NaN);
			_terrainWithoutStuff = terrainValues;
			_thingWithStuff = new Dictionary<ThingDef, float>[thingCount];
			_thingWithoutStuff = thingValues;
			return true;
		}

		internal static bool TryGet(BuildableDef def, ThingDef stuff, out float value)
		{
			value = 0f;
			if (_thingWithoutStuff == null && !EnsureArrays())
				return false;

			if (def is ThingDef thingDef)
			{
				var index = thingDef.index;
				var values = _thingWithoutStuff;
				if (index >= values.Length)
					return false;

				if (stuff == null)
				{
					value = values[index];
					return !float.IsNaN(value);
				}

				var stuffValues = _thingWithStuff[index];
				return stuffValues != null && stuffValues.TryGetValue(stuff, out value);
			}

			if (def is TerrainDef terrainDef && stuff == null)
			{
				var index = terrainDef.index;
				var values = _terrainWithoutStuff;
				if (index >= values.Length)
					return false;

				value = values[index];
				return !float.IsNaN(value);
			}

			return false;
		}

		internal static void Store(BuildableDef def, ThingDef stuff, float value)
		{
			if (_thingWithoutStuff == null && !EnsureArrays())
				return;

			if (def is ThingDef thingDef)
			{
				var index = thingDef.index;
				var values = _thingWithoutStuff;
				if (index >= values.Length)
					return;

				if (stuff == null)
				{
					values[index] = value;
					return;
				}

				var stuffValues = _thingWithStuff[index];
				if (stuffValues == null)
				{
					stuffValues = new Dictionary<ThingDef, float>();
					_thingWithStuff[index] = stuffValues;
				}
				stuffValues[stuff] = value;
				return;
			}

			if (def is TerrainDef terrainDef && stuff == null)
			{
				var index = terrainDef.index;
				var values = _terrainWithoutStuff;
				if (index < values.Length)
					values[index] = value;
			}
		}
	}

	/// <summary>
	/// Caches <see cref="Frame.WorkToBuild"/> by its buildable def and stuff.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFrameWorkToBuildCache"/>
	[HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
	internal static class RimWorld_Frame_WorkToBuild
	{
		static bool Prepare()
		{
			FrameWorkToBuildCache.Invalidate();
			return YaOptGlobal.Settings?.OptFrameWorkToBuildCache.Enabled == true;
		}

		static bool Prefix(Frame __instance, ref float __result)
		{
			var buildable = __instance?.def?.entityDefToBuild;
			return buildable == null || !FrameWorkToBuildCache.TryGet(buildable, __instance.Stuff, out __result);
		}

		static void Postfix(Frame __instance, float __result, bool __runOriginal)
		{
			var buildable = __instance?.def?.entityDefToBuild;
			if (!__runOriginal || buildable == null)
				return;
			FrameWorkToBuildCache.Store(buildable, __instance.Stuff, __result);
		}
	}
}
