using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Flattens VFE Insectoids' settlement territories so non-territory tiles skip the vanilla scan.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptWorldMapInsectLayer"/>
	[HarmonyPatch]
	[HarmonyPriority(Priority.First)]
	internal static class VFEInsectoids_WorldDrawLayer_Insects_TryAddMeshForTile
	{
		private static FieldInfo _instanceField;
		private static FieldInfo _insectTilesField;
		private static FieldInfo _territoryTilesField;
		private static HashSet<int> _tiles;
		private static int _builtTick = int.MinValue;
		private static int _builtWorld;
		private static int _builtTerritories = -1;
		private static int _builtSettlements = -1;

		static VFEInsectoids_WorldDrawLayer_Insects_TryAddMeshForTile()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCache);
		}

		private static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("VFEInsectoids.WorldDrawLayer_Insects");
			return type == null ? null : AccessTools.Method(type, "TryAddMeshForTile", new[] { typeof(PlanetTile) });
		}

		internal static bool IsAvailable()
		{
			var component = AccessTools.TypeByName("VFEInsectoids.GameComponent_Insectoids");
			var territory = AccessTools.TypeByName("VFEInsectoids.InsectTerritory");
			return TargetMethod() != null && component != null && territory != null &&
				   AccessTools.Field(component, "Instance") != null &&
				   AccessTools.Field(component, "insectTiles") != null &&
				   AccessTools.Field(territory, "tiles") != null;
		}

		private static bool Prepare()
		{
			if (!WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapInsectLayer))
				return false;

			var component = AccessTools.TypeByName("VFEInsectoids.GameComponent_Insectoids");
			var territory = AccessTools.TypeByName("VFEInsectoids.InsectTerritory");
			if (component == null || territory == null)
				return false;

			_instanceField = AccessTools.Field(component, "Instance");
			_insectTilesField = AccessTools.Field(component, "insectTiles");
			_territoryTilesField = AccessTools.Field(territory, "tiles");
			return _instanceField != null && _insectTilesField != null && _territoryTilesField != null;
		}

		private static bool Prefix(PlanetTile __0, ref bool __result)
		{
			if (!TryGetTiles(out var set))
				return true;

			if (set.Count == 0 || !set.Contains(__0.tileId))
			{
				__result = false;
				return false;
			}

			return true;
		}

		private static bool TryGetTiles(out HashSet<int> result)
		{
			result = null;
			var component = _instanceField?.GetValue(null);
			if (component == null || !(_insectTilesField?.GetValue(component) is IDictionary territories))
				return false;

			var settlements = Find.WorldObjects?.Settlements;
			if (settlements == null)
				return false;

			var tick = Find.TickManager?.TicksGame ?? -1;
			var world = Find.World;
			var worldId = world == null ? 0 : RuntimeHelpers.GetHashCode(world);
			if (_tiles != null && tick == _builtTick && worldId == _builtWorld &&
				territories.Count == _builtTerritories && settlements.Count == _builtSettlements)
			{
				result = _tiles;
				return true;
			}

			// A null insect faction would make the vanilla predicate match null-faction settlements.
			var insectFaction = Faction.OfInsects;
			if (insectFaction == null)
				return false;

			var next = new HashSet<int>();
			for (var i = 0; i < settlements.Count; i++)
			{
				var settlement = settlements[i];
				if (settlement == null || settlement.Faction != insectFaction)
					continue;

				var territory = territories[settlement];
				if (territory == null)
					continue;

				if (_territoryTilesField?.GetValue(territory) is IEnumerable<int> ids)
				{
					foreach (var id in ids)
						next.Add(id);
				}
				else if (_territoryTilesField?.GetValue(territory) is IEnumerable values)
				{
					foreach (var value in values)
					{
						if (value is int id)
							next.Add(id);
					}
				}
			}

			_tiles = next;
			_builtTick = tick;
			_builtWorld = worldId;
			_builtTerritories = territories.Count;
			_builtSettlements = settlements.Count;
			result = next;
			return true;
		}

		internal static void ClearCache()
		{
			_tiles = null;
			_builtTick = int.MinValue;
			_builtTerritories = -1;
			_builtSettlements = -1;
		}
	}
}
