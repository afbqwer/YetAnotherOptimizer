using HarmonyLib;
using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Per-room state behind the <see cref="Room.ContainedAndAdjacentThings"/> cache and the room
	/// stats/role throttle.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="Room.ContainedAndAdjacentThings"/> rebuilds its list on every access: it clears the
	/// backing set and list, walks every region of the room and re-adds each thing through a
	/// <see cref="HashSet{T}"/>. A single <c>UpdateRoomStatsAndRole</c> asks for it a dozen times,
	/// because most <c>RoomStatWorker</c> and <c>RoomRoleWorker</c> implementations read it.
	/// </para>
	/// <para>
	/// The content is a pure function of the room's region set and of the things listed in those
	/// regions, so <c>Region.id</c> plus <c>List&lt;Thing&gt;._version</c> detects every change.
	/// Entries are keyed on <see cref="Room.ID"/> but hold the room through a weak reference, and are
	/// swept periodically, so a discarded map cannot be kept alive by a stale snapshot.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRoomThingsCache"/>
	/// <seealso cref="YaOptSettings.OptRoomStatsThrottle"/>
	internal static class RoomContentCache
	{
		/// <summary>
		/// How long a room may keep serving its previous stats and role after something in it changed.
		/// </summary>
		internal const int StatsUpdateInterval = 128;

		private const int PruneInterval = 4096;

		private static readonly AccessTools.FieldRef<List<Thing>, int> ListVersion = CreateVersionRef();

		private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

		private static int _lastPruneTick;

		internal sealed class Entry
		{
			public System.WeakReference<Room> Owner;

			public readonly List<Thing> Things = new List<Thing>();

			public int[] RegionIds = Array.Empty<int>();

			public List<Thing>[] RegionLists = Array.Empty<List<Thing>>();

			public int[] RegionVersions = Array.Empty<int>();

			public int NextStatsTick;
		}

		static RoomContentCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		/// <summary>
		/// Returns false when the runtime does not expose the list version counter, in which case the
		/// patch must stay disabled rather than serve stale room contents.
		/// </summary>
		internal static bool IsSupported => ListVersion != null;

		private static AccessTools.FieldRef<List<Thing>, int> CreateVersionRef()
		{
			try
			{
				return AccessTools.FieldRefAccess<int>(typeof(List<Thing>), "_version");
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Entry GetOrCreateEntry(Room room)
		{
			var id = room.ID;
			if (Entries.TryGetValue(id, out var entry) && IsOwner(entry, room))
				return entry;

			entry = new Entry { Owner = new System.WeakReference<Room>(room) };
			Entries[id] = entry;
			return entry;
		}

		private static bool IsOwner(Entry entry, Room room)
		{
			var owner = entry.Owner;
			return owner != null && owner.TryGetTarget(out var cached) && ReferenceEquals(cached, room);
		}

		/// <summary>
		/// Returns the snapshot stored for this room, or false when the room, its region set or any of
		/// the region thing lists moved since the snapshot was taken.
		/// </summary>
		internal static bool TryGetThings(Room room, out List<Thing> snapshot)
		{
			snapshot = null;
			if (room.ID < 0)
				return false;
			if (!Entries.TryGetValue(room.ID, out var entry) || !IsOwner(entry, room))
				return false;

			var regions = room.Regions;
			if (regions.Count != entry.RegionIds.Length)
				return false;

			for (var i = 0; i < regions.Count; i++)
			{
				var region = regions[i];
				if (region == null || region.id != entry.RegionIds[i])
					return false;

				var allThings = region.ListerThings.AllThings;
				if (allThings == null
					|| !ReferenceEquals(allThings, entry.RegionLists[i])
					|| ListVersion(allThings) != entry.RegionVersions[i])
					return false;
			}

			snapshot = entry.Things;
			return true;
		}

		internal static void StoreThings(Room room, List<Thing> things)
		{
			if (room.ID < 0)
				return;

			var entry = GetOrCreateEntry(room);
			entry.Things.Clear();
			entry.Things.AddRange(things);

			var regions = room.Regions;
			if (entry.RegionIds.Length != regions.Count)
			{
				entry.RegionIds = new int[regions.Count];
				entry.RegionLists = new List<Thing>[regions.Count];
				entry.RegionVersions = new int[regions.Count];
			}

			for (var i = 0; i < regions.Count; i++)
			{
				var region = regions[i];
				entry.RegionIds[i] = region?.id ?? -1;

				var allThings = region?.ListerThings.AllThings;
				entry.RegionLists[i] = allThings;
				entry.RegionVersions[i] = allThings != null ? ListVersion(allThings) : -1;
			}

			// Keeps the dictionary from retaining rooms of discarded maps when only the content cache
			// is enabled and the throttle never runs.
			PruneIfDue(GenTicks.TicksGame);
		}

		/// <summary>
		/// Returns true when this room is allowed to recompute its stats and role now, and opens the
		/// next window. The first computation of a room is never throttled - callers check
		/// <c>role == null</c> before asking.
		/// </summary>
		internal static bool TryConsumeStatsUpdate(Room room)
		{
			if (room.ID < 0)
				return true;

			var now = GenTicks.TicksGame;
			var entry = GetOrCreateEntry(room);
			if (now < entry.NextStatsTick)
				return false;

			entry.NextStatsTick = now + StatsUpdateInterval;
			PruneIfDue(now);
			return true;
		}

		private static void PruneIfDue(int now)
		{
			if (now - _lastPruneTick < PruneInterval)
				return;

			_lastPruneTick = now;
			List<int> dead = null;

			foreach (var pair in Entries)
			{
				var owner = pair.Value.Owner;
				if (owner != null && owner.TryGetTarget(out _))
					continue;

				(dead ??= new List<int>()).Add(pair.Key);
			}

			if (dead == null)
				return;

			for (var i = 0; i < dead.Count; i++)
				Entries.Remove(dead[i]);
		}

		internal static void Clear()
		{
			Entries.Clear();
			_lastPruneTick = 0;
		}
	}
}
