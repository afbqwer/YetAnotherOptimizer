using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Tick-local cache for the current map's celestial sun glow.
	/// </summary>
	internal static class CelestialSunGlowCache
	{
		private static readonly ConditionalWeakTable<Map, Entry> Entries = new ConditionalWeakTable<Map, Entry>();

		static CelestialSunGlowCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		internal static void Clear()
		{
			Entries.Clear();
		}

		internal static bool TryGet(Map map, int tick, out float value)
		{
			if (!Entries.TryGetValue(map, out var entry))
			{
				value = 0f;
				return false;
			}

			var snapshot = Interlocked.Read(ref entry.Snapshot);
			if (snapshot == long.MinValue || (int)(snapshot >> 32) != tick)
			{
				value = 0f;
				return false;
			}

			value = BitConverter.Int32BitsToSingle(unchecked((int)snapshot));
			return true;
		}

		internal static void Store(Map map, int tick, float value)
		{
			var entry = Entries.GetValue(map, static _ => new Entry());
			var snapshot = ((long)(uint)tick << 32) | (uint)BitConverter.SingleToInt32Bits(value);
			Interlocked.Exchange(ref entry.Snapshot, snapshot);
		}

		private sealed class Entry
		{
			/// <summary>Packs the absolute tick and float bits into one atomically replaced value.</summary>
			internal long Snapshot = long.MinValue;
		}
	}

	/// <summary>
	/// Caches <see cref="GenCelestial.CurCelestialSunGlow"/> for the remainder of the current tick.
	/// </summary>
	/// <remarks>
	/// Only the overload that reads the current tick is patched. Map identity and TicksAbs fully key
	/// the result; a gravship's inactive map does not need a Tile guard.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptCelestialSunGlowCache"/>
	[HarmonyPatch(typeof(GenCelestial), nameof(GenCelestial.CurCelestialSunGlow), typeof(Map))]
	internal static class RimWorld_GenCelestial_CurCelestialSunGlow
	{
		static bool Prepare()
		{
			CelestialSunGlowCache.Clear();
			return YaOptGlobal.Settings?.OptCelestialSunGlowCache.Enabled == true;
		}

		static bool Prefix(Map map, ref float __result)
		{
			return map == null || !CelestialSunGlowCache.TryGet(map, Find.TickManager.TicksAbs, out __result);
		}

		static void Postfix(Map map, float __result, bool __runOriginal)
		{
			if (!__runOriginal || map == null)
				return;
			CelestialSunGlowCache.Store(map, Find.TickManager.TicksAbs, __result);
		}
	}
}
