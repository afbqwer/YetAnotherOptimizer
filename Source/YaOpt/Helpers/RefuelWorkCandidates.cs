using RimWorld;
using System.Collections.Generic;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Maintained per-map list of refuelable things that can currently be auto-refueled.
	/// </summary>
	/// <remarks>
	/// Refuel work givers normally have no global candidate set, so every job that finds nothing
	/// in the local region scan falls back to walking the whole map. This list is rebuilt every
	/// 250 ticks and updated on spawn, despawn, refuel and fuel consumption. All candidate kinds
	/// are kept in one list: the work giver's own <c>CanRefuelThing</c> still filters turrets and
	/// non-turrets, so a shared list cannot omit either kind.
	/// </remarks>
	public static class RefuelWorkCandidates
	{
		private const int REBUILD_INTERVAL = 250;

		public sealed class State
		{
			public readonly List<Thing> All = new List<Thing>();

			public readonly List<Thing> NonTurrets = new List<Thing>();

			public readonly List<Thing> Turrets = new List<Thing>();

			public readonly HashSet<Thing> Set = new HashSet<Thing>();

			public int LastRebuildTick = -99999;
		}

		public static List<Thing> GetCandidates(Map map, WorkGiver_Scanner scanner)
		{
			var tick = Find.TickManager.TicksGame;
			var state = GetOrCreateState(map);
			if (tick < state.LastRebuildTick || tick - state.LastRebuildTick >= REBUILD_INTERVAL)
				Rebuild(map, state, tick);

			var type = scanner.GetType();
			if (type == typeof(WorkGiver_Refuel))
				return state.NonTurrets;
			if (type == typeof(WorkGiver_Refuel_Turret))
				return state.Turrets;
			return state.All;
		}

		public static void NotifyChanged(CompRefuelable comp)
		{
			var thing = comp.parent;
			if (thing == null || !thing.Spawned)
				return;
			ref var state = ref PrepatcherFields.RefuelCandidates(thing.Map);
			if (state == null)
				return;

			if (CanBeCandidate(thing, comp))
				Add(state, thing);
			else
				Remove(state, thing);
		}

		public static void NotifyFuelConsumed(CompRefuelable comp)
		{
			if (comp.FuelPercentOfTarget > comp.Props.autoRefuelPercent)
				return;
			NotifyChanged(comp);
		}

		public static void NotifyRemoved(CompRefuelable comp, Map map)
		{
			if (map == null)
				return;
			ref var state = ref PrepatcherFields.RefuelCandidates(map);
			if (state == null)
				return;

			var thing = comp.parent;
			if (thing != null)
				Remove(state, thing);
		}

		private static bool CanBeCandidate(Thing thing, CompRefuelable comp)
		{
			if (!thing.Spawned || thing.Fogged() || !comp.allowAutoRefuel)
				return false;
			if (comp.FuelPercentOfMax > 0f && !comp.Props.allowRefuelIfNotEmpty)
				return false;
			return comp.ShouldAutoRefuelNow;
		}

		private static void Add(State state, Thing thing)
		{
			if (!state.Set.Add(thing))
				return;
			state.All.Add(thing);
			if (thing is Building_Turret)
				state.Turrets.Add(thing);
			else
				state.NonTurrets.Add(thing);
		}

		private static void Remove(State state, Thing thing)
		{
			if (!state.Set.Remove(thing))
				return;
			state.All.Remove(thing);
			if (thing is Building_Turret)
				state.Turrets.Remove(thing);
			else
				state.NonTurrets.Remove(thing);
		}

		private static void Rebuild(Map map, State state, int tick)
		{
			state.All.Clear();
			state.NonTurrets.Clear();
			state.Turrets.Clear();
			state.Set.Clear();

			var refuelables = map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable);
			for (var i = 0; i < refuelables.Count; i++)
			{
				var thing = refuelables[i];
				var comp = thing.TryGetComp<CompRefuelable>();
				if (comp == null || !CanBeCandidate(thing, comp))
					continue;
				Add(state, thing);
			}

			state.LastRebuildTick = tick;
		}

		private static State GetOrCreateState(Map map)
		{
			ref var state = ref PrepatcherFields.RefuelCandidates(map);
			if (state == null)
				state = new State();
			return state;
		}
	}
}