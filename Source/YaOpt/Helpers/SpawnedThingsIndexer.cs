using HarmonyLib;
using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// O(1) index for <see cref="Map.spawnedThings"/> removals.
	/// </summary>
	/// <remarks>
	/// <see cref="ThingOwner{T}.Remove"/> still performs a <c>LastIndexOf</c> and a list shift on every
	/// despawn. This index replaces both with a dictionary lookup and a swap-remove, but only for
	/// the map-owned spawned-things list.
	/// </remarks>
	internal static class SpawnedThingsIndexer
	{
		private static readonly Dictionary<Map, Dictionary<Thing, int>> Indices =
			new Dictionary<Map, Dictionary<Thing, int>>();

		static SpawnedThingsIndexer()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearAll);
		}

		/// <summary>
		/// Attempts an indexed removal. Returns false when the vanilla path should run instead.
		/// </summary>
		public static bool TryRemove(ThingOwner<Thing> owner, Thing item, out bool result)
		{
			result = false;
			if (!IsTargetOwner(owner, out var map))
				return false;

			// ThingOwner.Contains is an O(1) holdingOwner check.
			if (!owner.Contains(item))
				return true;

			var list = owner.InnerListForReading;
			var index = GetOrBuild(map, list);
			if (!TryGetIndex(index, list, item, out var i))
			{
				Rebuild(index, list);
				if (!TryGetIndex(index, list, item, out i))
					return false;
			}

			if (ReferenceEquals(item.holdingOwner, owner))
				item.holdingOwner = null;

			var last = list.Count - 1;
			if (i != last)
			{
				var moved = list[last];
				list[i] = moved;
				index[moved] = i;
			}

			list.RemoveAt(last);
			index.Remove(item);
			// ThingOwner.NotifyRemoved is protected in vanilla; publicizer lets us call it directly.
			owner.NotifyRemoved(item);
			result = true;
			return true;
		}

		public static void OnAdded(ThingOwner<Thing> owner, Thing item)
		{
			if (!IsTargetOwner(owner, out var map))
				return;

			var list = owner.InnerListForReading;
			var index = GetOrBuild(map, list);

			if (!ReferenceEquals(item.holdingOwner, owner))
				return;

			var last = list.Count - 1;
			if (last >= 0 && ReferenceEquals(list[last], item))
				index[item] = last;
			else
				Rebuild(index, list);
		}

		public static void OnCleared(ThingOwner owner)
		{
			if (owner.Owner is Map map && ReferenceEquals(map.spawnedThings, owner))
				Indices.Remove(map);
		}

		private static bool IsTargetOwner(ThingOwner<Thing> owner, out Map map)
		{
			map = owner?.Owner as Map;
			return map != null && ReferenceEquals(map.spawnedThings, owner);
		}

		private static Dictionary<Thing, int> GetOrBuild(Map map, List<Thing> list)
		{
			if (Indices.TryGetValue(map, out var index))
				return index;

			index = new Dictionary<Thing, int>(list.Count);
			Rebuild(index, list);
			Indices[map] = index;
			return index;
		}

		private static void Rebuild(Dictionary<Thing, int> index, List<Thing> list)
		{
			index.Clear();
			for (var i = 0; i < list.Count; i++)
			{
				var thing = list[i];
				if (thing != null)
					index[thing] = i;
			}
		}

		private static bool TryGetIndex(Dictionary<Thing, int> index, List<Thing> list, Thing item,
			out int i)
		{
			return index.TryGetValue(item, out i) &&
				   i >= 0 &&
				   i < list.Count &&
				   ReferenceEquals(list[i], item);
		}

		private static void ClearAll()
		{
			Indices.Clear();
		}
	}
}