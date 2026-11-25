using RimWorld;
using System.Collections.Generic;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Per-map index of colonist buildings by <see cref="ThingDef"/>.
	/// </summary>
	/// <remarks>
	/// Vanilla scans every colonist building for each definition query. The index is built lazily
	/// on the first query and then maintained by <c>ListerBuildings.Add/Remove</c>. Query methods
	/// return the cached list directly, so repeated lookups of the same definition are O(1).
	/// </remarks>
	public static class ListerBuildingsCache
	{
		public sealed class State
		{
			public readonly Dictionary<ThingDef, List<Building>> ByDef =
				new Dictionary<ThingDef, List<Building>>();

			public bool Built;
		}

		private static readonly List<Building> Empty = new List<Building>();

		public static bool ColonistsHaveBuilding(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return false;

			var cache = GetOrBuild(lister);
			return cache.ByDef.TryGetValue(def, out var list) && list.Count > 0;
		}

		public static List<Building> GetAllBuildingsColonistOfDef(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return Empty;

			var cache = GetOrBuild(lister);
			return cache.ByDef.TryGetValue(def, out var list) ? list : Empty;
		}

		public static bool ColonistsHaveBuildingWithPowerOn(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return false;

			var cache = GetOrBuild(lister);
			if (!cache.ByDef.TryGetValue(def, out var list))
				return false;

			for (var i = 0; i < list.Count; i++)
			{
				var power = list[i].TryGetComp<CompPowerTrader>();
				if (power == null || power.PowerOn)
					return true;
			}
			return false;
		}

		public static void OnAdded(ListerBuildings lister, Building building)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			if (cache == null || !cache.Built)
				return;
			if (building.Faction != Faction.OfPlayer)
				return;
			var props = building.def.building;
			if (props != null && props.isNaturalRock)
				return;

			if (!cache.ByDef.TryGetValue(building.def, out var list))
			{
				list = new List<Building>();
				cache.ByDef.Add(building.def, list);
			}
			list.Add(building);
		}

		public static void OnRemoved(ListerBuildings lister, Building building)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			if (cache == null || !cache.Built)
				return;
			if (!cache.ByDef.TryGetValue(building.def, out var list))
				return;

			list.Remove(building);
			if (list.Count == 0)
				cache.ByDef.Remove(building.def);
		}

		public static void OnDisposed(ListerBuildings lister)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			cache = null;
		}

		private static State GetOrBuild(ListerBuildings lister)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			if (cache == null)
				cache = new State();
			else if (cache.Built)
				return cache;

			cache.ByDef.Clear();
			for (var i = 0; i < lister.allBuildingsColonist.Count; i++)
			{
				var building = lister.allBuildingsColonist[i];
				if (!cache.ByDef.TryGetValue(building.def, out var list))
				{
					list = new List<Building>();
					cache.ByDef.Add(building.def, list);
				}
				list.Add(building);
			}
			cache.Built = true;
			return cache;
		}
	}
}