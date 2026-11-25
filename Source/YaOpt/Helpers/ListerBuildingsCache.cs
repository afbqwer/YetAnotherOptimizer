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
	/// on the first query and then maintained by <c>ListerBuildings.Add/Remove</c>. List results
	/// are copied into the vanilla result buffer so callers cannot mutate the index.
	/// </remarks>
	public static class ListerBuildingsCache
	{
		private static int _epoch;

		public sealed class State
		{
			public readonly Dictionary<ThingDef, List<Building>> ByDef =
				new Dictionary<ThingDef, List<Building>>();

			public bool Built;

			public int Epoch;
		}

		public static bool ColonistsHaveBuilding(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return false;

			var list = GetOrBuild(lister, def);
			return list.Count > 0;
		}

		public static List<Building> GetAllBuildingsColonistOfDef(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return GetResultBuffer();

			var buildings = GetOrBuild(lister, def);
			var result = GetResultBuffer();
			result.AddRange(buildings);
			return result;
		}

		public static bool ColonistsHaveBuildingWithPowerOn(ListerBuildings lister, ThingDef def)
		{
			if (def == null)
				return false;

			var list = GetOrBuild(lister, def);
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
		}

		public static void OnDisposed(ListerBuildings lister)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			cache = null;
		}

		public static void Invalidate()
		{
			_epoch++;
		}

		private static List<Building> GetResultBuffer()
		{
			var result = ListerBuildings.allBuildingsColonistOfDefResult;
			result.Clear();
			return result;
		}

		private static List<Building> GetOrBuild(ListerBuildings lister, ThingDef def)
		{
			ref var cache = ref PrepatcherFields.Lister(lister);
			if (cache == null)
				cache = new State();
			else if (cache.Built && cache.Epoch == _epoch &&
				cache.ByDef.TryGetValue(def, out var cached))
				return cached;

			if (!cache.Built || cache.Epoch != _epoch)
			{
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
				cache.Epoch = _epoch;
			}

			if (!cache.ByDef.TryGetValue(def, out var buildings))
			{
				buildings = new List<Building>();
				cache.ByDef.Add(def, buildings);
			}
			return buildings;
		}
	}
}