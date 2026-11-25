using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Tick-local cache shared by the <see cref="GenLocalDate"/> patches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every <c>GenLocalDate</c> lookup ends in <c>GenDate.*(GenTicks.TicksAbs, longitude)</c>, and the
	/// longitude comes from <c>Find.WorldGrid.LongLatOf</c>, which rebuilds the tile centre from the
	/// planet mesh on every call and has no cache in 1.7. Within one tick both inputs are constant, so
	/// the same map yields the same value dozens of times per tick - <c>Map.MapUpdate</c> alone asks for
	/// it through <c>SkyManager</c>, <c>WeatherWorker</c> and <c>GenCelestial</c>.
	/// </para>
	/// <para>
	/// Keyed on <see cref="Map.uniqueID"/> rather than the <see cref="Map"/> instance so that unloading a
	/// map cannot leak it, and on the absolute tick rather than <c>TicksGame</c> because
	/// <c>GenTicks.TicksAbs</c> takes a different branch while not in <c>ProgramState.Playing</c>.
	/// </para>
	/// <para>
	/// Concurrent access is tolerated without locking. A torn read can only mix fields from two adjacent
	/// ticks, which is off by at most one tick out of 60000 per day.
	/// </para>
	/// </remarks>
	internal static class GenLocalDateCache
	{
		private const int SlotCount = 4;

		private const int SlotMask = SlotCount - 1;

		private const int EmptyAbs = int.MinValue;

		private const int EmptyMapId = int.MinValue;

		private struct Snapshot
		{
			public int MapId;

			public int DayTickAbs;

			public int HourIntegerAbs;

			public int DayPercentAbs;

			public int SeasonAbs;

			public int DayTick;

			public int HourInteger;

			public float DayPercent;

			public Season Season;
		}

		private static readonly Snapshot Empty = new Snapshot
		{
			MapId = EmptyMapId,
			DayTickAbs = EmptyAbs,
			HourIntegerAbs = EmptyAbs,
			DayPercentAbs = EmptyAbs,
			SeasonAbs = EmptyAbs
		};

		private static readonly Snapshot[] _snapshots = new Snapshot[SlotCount];

		static GenLocalDateCache()
		{
			for (var i = 0; i < SlotCount; i++)
				_snapshots[i] = Empty;
		}

		/// <summary>
		/// Picks the slot for a map and wipes it if the slot currently belongs to another map.
		/// Without the wipe the per-field tick stamps of the previous map would be reused by the new one.
		/// </summary>
		private static ref Snapshot Prepare(int mapId)
		{
			ref var snapshot = ref _snapshots[mapId & SlotMask];
			if (snapshot.MapId != mapId)
			{
				snapshot = Empty;
				snapshot.MapId = mapId;
			}
			return ref snapshot;
		}

		internal static bool TryGetDayTick(int mapId, int absTick, out int value)
		{
			ref var snapshot = ref _snapshots[mapId & SlotMask];
			if (snapshot.MapId == mapId && snapshot.DayTickAbs == absTick)
			{
				value = snapshot.DayTick;
				return true;
			}
			value = 0;
			return false;
		}

		internal static void StoreDayTick(int mapId, int absTick, int value)
		{
			ref var snapshot = ref Prepare(mapId);
			snapshot.DayTickAbs = absTick;
			snapshot.DayTick = value;
		}

		internal static bool TryGetHourInteger(int mapId, int absTick, out int value)
		{
			ref var snapshot = ref _snapshots[mapId & SlotMask];
			if (snapshot.MapId == mapId && snapshot.HourIntegerAbs == absTick)
			{
				value = snapshot.HourInteger;
				return true;
			}
			value = 0;
			return false;
		}

		internal static void StoreHourInteger(int mapId, int absTick, int value)
		{
			ref var snapshot = ref Prepare(mapId);
			snapshot.HourIntegerAbs = absTick;
			snapshot.HourInteger = value;
		}

		internal static bool TryGetDayPercent(int mapId, int absTick, out float value)
		{
			ref var snapshot = ref _snapshots[mapId & SlotMask];
			if (snapshot.MapId == mapId && snapshot.DayPercentAbs == absTick)
			{
				value = snapshot.DayPercent;
				return true;
			}
			value = 0f;
			return false;
		}

		internal static void StoreDayPercent(int mapId, int absTick, float value)
		{
			ref var snapshot = ref Prepare(mapId);
			snapshot.DayPercentAbs = absTick;
			snapshot.DayPercent = value;
		}

		internal static bool TryGetSeason(int mapId, int absTick, out Season value)
		{
			ref var snapshot = ref _snapshots[mapId & SlotMask];
			if (snapshot.MapId == mapId && snapshot.SeasonAbs == absTick)
			{
				value = snapshot.Season;
				return true;
			}
			value = Season.Undefined;
			return false;
		}

		internal static void StoreSeason(int mapId, int absTick, Season value)
		{
			ref var snapshot = ref Prepare(mapId);
			snapshot.SeasonAbs = absTick;
			snapshot.Season = value;
		}
	}

	/// <summary>
	/// Caches <see cref="GenLocalDate.DayTick(Map)"/>, <see cref="GenLocalDate.HourInteger(Map)"/>,
	/// <see cref="GenLocalDate.DayPercent(Map)"/> and <see cref="GenLocalDate.Season(Map)"/> for the
	/// remainder of the current tick.
	/// </summary>
	/// <remarks>
	/// <c>DayPercent</c> matters most: <c>SkyManager.SkyManagerUpdate</c> runs it for the current map on
	/// every tick, and it does not pass through <c>GenLocalDate.DayTick</c> - it calls
	/// <c>GenDate.DayPercent</c> directly, so caching <c>DayTick</c> alone would not cover it.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptLocalDateCache"/>
	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.DayTick), typeof(Map))]
	internal static class RimWorld_GenLocalDate_DayTick_Map
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Map map, ref int __result)
		{
			if (map == null)
				return true;
			return !GenLocalDateCache.TryGetDayTick(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Map map, int __result, bool __runOriginal)
		{
			if (!__runOriginal || map == null)
				return;
			GenLocalDateCache.StoreDayTick(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.HourInteger), typeof(Map))]
	internal static class RimWorld_GenLocalDate_HourInteger_Map
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Map map, ref int __result)
		{
			if (map == null)
				return true;
			return !GenLocalDateCache.TryGetHourInteger(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Map map, int __result, bool __runOriginal)
		{
			if (!__runOriginal || map == null)
				return;
			GenLocalDateCache.StoreHourInteger(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.DayPercent), typeof(Map))]
	internal static class RimWorld_GenLocalDate_DayPercent_Map
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Map map, ref float __result)
		{
			if (map == null)
				return true;
			return !GenLocalDateCache.TryGetDayPercent(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Map map, float __result, bool __runOriginal)
		{
			if (!__runOriginal || map == null)
				return;
			GenLocalDateCache.StoreDayPercent(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.Season), typeof(Map))]
	internal static class RimWorld_GenLocalDate_Season_Map
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Map map, ref Season __result)
		{
			if (map == null)
				return true;
			return !GenLocalDateCache.TryGetSeason(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Map map, Season __result, bool __runOriginal)
		{
			if (!__runOriginal || map == null)
				return;
			GenLocalDateCache.StoreSeason(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	/// <summary>
	/// Routes the <see cref="Thing"/> overloads through the same per-map cache.
	/// </summary>
	/// <remarks>
	/// A spawned thing resolves its date from <c>Map.Tile</c>, so the value is identical to the map
	/// overload. Pocket maps are excluded on purpose: there <c>Thing.Tile</c> is invalid and
	/// <c>GenLocalDate.LocationForDate</c> falls back to longitude zero, while <c>GenLocalDate.DayTick(Map)</c>
	/// falls back to <c>Find.AnyPlayerHomeMap</c>'s tile. The two disagree, so those maps must keep vanilla
	/// behaviour.
	/// </remarks>
	internal static class GenLocalDateThingHelper
	{
		internal static bool TryGetMap(Thing thing, out Map map)
		{
			map = null;
			if (thing == null || !thing.Spawned)
				return false;
			map = thing.Map;
			return map != null && map.Tile.Valid;
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.DayTick), typeof(Thing))]
	internal static class RimWorld_GenLocalDate_DayTick_Thing
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Thing thing, ref int __result)
		{
			return !GenLocalDateThingHelper.TryGetMap(thing, out var map)
				|| !GenLocalDateCache.TryGetDayTick(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Thing thing, int __result, bool __runOriginal)
		{
			if (!__runOriginal || !GenLocalDateThingHelper.TryGetMap(thing, out var map))
				return;
			GenLocalDateCache.StoreDayTick(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.HourInteger), typeof(Thing))]
	internal static class RimWorld_GenLocalDate_HourInteger_Thing
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Thing thing, ref int __result)
		{
			return !GenLocalDateThingHelper.TryGetMap(thing, out var map)
				|| !GenLocalDateCache.TryGetHourInteger(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Thing thing, int __result, bool __runOriginal)
		{
			if (!__runOriginal || !GenLocalDateThingHelper.TryGetMap(thing, out var map))
				return;
			GenLocalDateCache.StoreHourInteger(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}

	[HarmonyPatch(typeof(GenLocalDate), nameof(GenLocalDate.DayPercent), typeof(Thing))]
	internal static class RimWorld_GenLocalDate_DayPercent_Thing
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptLocalDateCache.Enabled == true;
		}

		static bool Prefix(Thing thing, ref float __result)
		{
			return !GenLocalDateThingHelper.TryGetMap(thing, out var map)
				|| !GenLocalDateCache.TryGetDayPercent(map.uniqueID, GenTicks.TicksAbs, out __result);
		}

		static void Postfix(Thing thing, float __result, bool __runOriginal)
		{
			if (!__runOriginal || !GenLocalDateThingHelper.TryGetMap(thing, out var map))
				return;
			GenLocalDateCache.StoreDayPercent(map.uniqueID, GenTicks.TicksAbs, __result);
		}
	}
}
