using HarmonyLib;
using System;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Memoises the never-changing base statistics of <see cref="ThingDef"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>BaseMass</c>, <c>BaseFlammability</c> and <c>BaseMaxHitPoints</c> all funnel into
	/// <c>GetStatValueAbstract</c>, which walks <c>statBases</c> and then every
	/// <see cref="StatPart"/> of the stat. There is no def-level cache: the vanilla
	/// <c>StatWorker.immutableStatCache</c> and <c>temporaryStatCache</c> are both keyed on a
	/// <see cref="Thing"/>, which an abstract request does not have.
	/// </para>
	/// <para>
	/// Vanilla itself only reads these in def generation, map generation and debug tables, so the
	/// gain is entirely on the mod side - hauling, storage and weight calculation mods read
	/// <c>BaseMass</c> in their own hot loops. Storage is a flat array indexed by
	/// <see cref="Def.index"/> rather than a dictionary because with a cold property the lookup
	/// itself decides whether the patch wins or loses.
	/// </para>
	/// </remarks>
	internal static class ThingDefBaseStatCache
	{
		/// <summary>
		/// <c>NaN</c> marks an empty slot. No vanilla stat can legitimately be <c>NaN</c>, and a
		/// fourth array would double the cache-line traffic on every read.
		/// </summary>
		private const int EmptyHitPoints = int.MinValue;

		private static float[] _mass;

		private static float[] _flammability;

		private static int[] _maxHitPoints;

		/// <summary>
		/// Called from every patch's <c>Prepare</c>, i.e. once at startup and again on each game load.
		/// </summary>
		internal static void Invalidate()
		{
			_mass = null;
			_flammability = null;
			_maxHitPoints = null;
		}

		/// <summary>
		/// Sizes the arrays to the current def count, growing or shrinking the cache after a def reload.
		/// </summary>
		/// <returns>The number of cache slots, or 0 when caching is not possible yet.</returns>
		private static int EnsureArrays()
		{
			// Before a game exists the def list is still being edited: ThingDefGenerator_Buildings
			// rewrites stat bases of the defs it creates, so caching here would freeze intermediate values.
			if (Current.Game == null)
				return 0;

			var count = DefDatabase<ThingDef>.DefCount;
			if (count == 0)
				return 0;

			var mass = _mass;
			if (mass != null
				&& mass.Length == count
				&& _flammability != null && _flammability.Length == count
				&& _maxHitPoints != null && _maxHitPoints.Length == count)
			{
				return count;
			}

			mass = new float[count];
			var flammability = new float[count];
			var hitPoints = new int[count];
			Array.Fill(mass, float.NaN);
			Array.Fill(flammability, float.NaN);
			Array.Fill(hitPoints, EmptyHitPoints);
			_flammability = flammability;
			_maxHitPoints = hitPoints;
			// Published last: a reader that sees a non-null _mass also sees the other two arrays.
			// Replacing the reference instead of clearing the old arrays keeps concurrent readers safe.
			_mass = mass;
			return count;
		}

		internal static bool TryGetMass(ThingDef def, out float value)
		{
			var cache = _mass;
			if (cache == null && EnsureArrays() == 0)
			{
				value = 0f;
				return false;
			}
			cache = _mass;
			if (cache == null)
			{
				value = 0f;
				return false;
			}
			var index = def.index;
			if (index >= cache.Length)
			{
				value = 0f;
				return false;
			}
			value = cache[index];
			return !float.IsNaN(value);
		}

		internal static void StoreMass(ThingDef def, float value)
		{
			var cache = _mass;
			if (cache == null)
				return;
			var index = def.index;
			if (index < cache.Length)
				cache[index] = value;
		}

		internal static bool TryGetFlammability(ThingDef def, out float value)
		{
			var cache = _flammability;
			if (cache == null && EnsureArrays() == 0)
			{
				value = 0f;
				return false;
			}
			cache = _flammability;
			if (cache == null)
			{
				value = 0f;
				return false;
			}
			var index = def.index;
			if (index >= cache.Length)
			{
				value = 0f;
				return false;
			}
			value = cache[index];
			return !float.IsNaN(value);
		}

		internal static void StoreFlammability(ThingDef def, float value)
		{
			var cache = _flammability;
			if (cache == null)
				return;
			var index = def.index;
			if (index < cache.Length)
				cache[index] = value;
		}

		internal static bool TryGetMaxHitPoints(ThingDef def, out int value)
		{
			var cache = _maxHitPoints;
			if (cache == null && EnsureArrays() == 0)
			{
				value = 0;
				return false;
			}
			cache = _maxHitPoints;
			if (cache == null)
			{
				value = 0;
				return false;
			}
			var index = def.index;
			if (index >= cache.Length)
			{
				value = 0;
				return false;
			}
			value = cache[index];
			return value != EmptyHitPoints;
		}

		internal static void StoreMaxHitPoints(ThingDef def, int value)
		{
			var cache = _maxHitPoints;
			if (cache == null)
				return;
			var index = def.index;
			if (index < cache.Length)
				cache[index] = value;
		}
	}

	/// <summary>
	/// Caches <see cref="ThingDef.BaseMass"/>.
	/// </summary>
	/// <remarks>
	/// Safe for an abstract request because every <see cref="StatPart"/> attached to Mass collapses to
	/// a def-level constant when no <see cref="Thing"/> is present: <c>StatPart_BodySize</c> reads
	/// <c>race.baseBodySize</c>, the other four return their identity (0 or 1).
	/// <para/>
	/// There is deliberately no version guard. <c>SetStatBaseValue</c> writes
	/// <c>statBases[i].value</c> in place, which does not bump <c>List&lt;T&gt;._version</c>, so a
	/// version check would give false confidence. A mod that rewrites stat bases after the game has
	/// started will see stale values until the next load.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptThingDefBaseStatCache"/>
	[HarmonyPatch(typeof(ThingDef), nameof(ThingDef.BaseMass), MethodType.Getter)]
	internal static class Verse_ThingDef_BaseMass
	{
		static bool Prepare()
		{
			ThingDefBaseStatCache.Invalidate();
			return YaOptGlobal.Settings?.OptThingDefBaseStatCache.Enabled == true;
		}

		static bool Prefix(ThingDef __instance, ref float __result)
		{
			return __instance == null
				|| !ThingDefBaseStatCache.TryGetMass(__instance, out __result);
		}

		static void Postfix(ThingDef __instance, float __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null)
				return;
			ThingDefBaseStatCache.StoreMass(__instance, __result);
		}
	}

	/// <summary>
	/// Caches <see cref="ThingDef.BaseFlammability"/>. Same reasoning as
	/// <see cref="Verse_ThingDef_BaseMass"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptThingDefBaseStatCache"/>
	[HarmonyPatch(typeof(ThingDef), nameof(ThingDef.BaseFlammability), MethodType.Getter)]
	internal static class Verse_ThingDef_BaseFlammability
	{
		static bool Prepare()
		{
			ThingDefBaseStatCache.Invalidate();
			return YaOptGlobal.Settings?.OptThingDefBaseStatCache.Enabled == true;
		}

		static bool Prefix(ThingDef __instance, ref float __result)
		{
			return __instance == null
				|| !ThingDefBaseStatCache.TryGetFlammability(__instance, out __result);
		}

		static void Postfix(ThingDef __instance, float __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null)
				return;
			ThingDefBaseStatCache.StoreFlammability(__instance, __result);
		}
	}

	/// <summary>
	/// Caches <see cref="ThingDef.BaseMaxHitPoints"/>. MaxHitPoints has no <see cref="StatPart"/> at
	/// all, so it is the least risky of the three.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptThingDefBaseStatCache"/>
	[HarmonyPatch(typeof(ThingDef), nameof(ThingDef.BaseMaxHitPoints), MethodType.Getter)]
	internal static class Verse_ThingDef_BaseMaxHitPoints
	{
		static bool Prepare()
		{
			ThingDefBaseStatCache.Invalidate();
			return YaOptGlobal.Settings?.OptThingDefBaseStatCache.Enabled == true;
		}

		static bool Prefix(ThingDef __instance, ref int __result)
		{
			return __instance == null
				|| !ThingDefBaseStatCache.TryGetMaxHitPoints(__instance, out __result);
		}

		static void Postfix(ThingDef __instance, int __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null)
				return;
			ThingDefBaseStatCache.StoreMaxHitPoints(__instance, __result);
		}
	}
}
