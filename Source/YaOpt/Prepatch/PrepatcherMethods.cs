using RimWorld;
using System.Collections.Generic;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Prepatch
{
	/// <summary>
	/// Bodies installed into vanilla methods by the built-in Prepatcher.
	/// </summary>
	/// <remarks>
	/// Each method keeps a vanilla fallback so a settings toggle only disables the optimization
	/// without changing the method's observable result.
	/// </remarks>
	public static class PrepatcherMethods
	{
		public static List<Building> AllBuildingsColonistOfDef(ListerBuildings lister, ThingDef def)
		{
			if (PrepatcherState.ListerBuildingsActive)
				return ListerBuildingsCache.GetAllBuildingsColonistOfDef(lister, def);

			var result = ListerBuildings.allBuildingsColonistOfDefResult;
			result.Clear();
			if (def == null)
				return result;

			for (var i = 0; i < lister.allBuildingsColonist.Count; i++)
			{
				var building = lister.allBuildingsColonist[i];
				if (building.def == def)
					result.Add(building);
			}
			return result;
		}

		public static bool ColonistsHaveBuilding(ListerBuildings lister, ThingDef def)
		{
			if (PrepatcherState.ListerBuildingsActive)
				return ListerBuildingsCache.ColonistsHaveBuilding(lister, def);

			if (def == null)
				return false;
			for (var i = 0; i < lister.allBuildingsColonist.Count; i++)
			{
				if (lister.allBuildingsColonist[i].def == def)
					return true;
			}
			return false;
		}

		public static bool ColonistsHaveBuildingWithPowerOn(ListerBuildings lister, ThingDef def)
		{
			if (PrepatcherState.ListerBuildingsActive)
				return ListerBuildingsCache.ColonistsHaveBuildingWithPowerOn(lister, def);
			if (def == null)
				return false;

			var buildings = AllBuildingsColonistOfDef(lister, def);
			for (var i = 0; i < buildings.Count; i++)
			{
				var power = buildings[i].TryGetComp<CompPowerTrader>();
				if (power == null || power.PowerOn)
					return true;
			}
			return false;
		}

		public static List<ImmunityHandler.ImmunityInfo> NeededImmunitiesNow(ImmunityHandler handler)
		{
			return ImmunityCache.NeededImmunitiesNow(handler);
		}

		public static IEnumerable<Thing> PotentialWorkThingsGlobal(WorkGiver_Scanner scanner, Pawn pawn)
		{
			if (!PrepatcherState.RefuelCandidatesActive ||
				!(scanner is WorkGiver_Refuel) ||
				pawn == null ||
				pawn.Map == null)
				return null;

			return RefuelWorkCandidates.GetCandidates(pawn.Map, scanner);
		}
	}
}