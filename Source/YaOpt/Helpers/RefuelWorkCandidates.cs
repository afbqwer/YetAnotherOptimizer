using RimWorld;
using System.Collections.Generic;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Maintained per-map list of things that can be considered for refueling.
	/// </summary>
	/// <remarks>
	/// The list is intentionally broad. Fuel level, auto-refuel settings, fog, reservations and
	/// other dynamic conditions are checked by the vanilla work giver when it validates a target.
	/// Only spawn and despawn need to update the list, avoiding missed candidates after state
	/// changes that have no dedicated notification.
	/// </remarks>
	public static class RefuelWorkCandidates
	{
		private static int _epoch;

		public sealed class State
		{
			public readonly List<Thing> All = new List<Thing>();

			public readonly List<Thing> NonTurrets = new List<Thing>();

			public readonly List<Thing> Turrets = new List<Thing>();

			public readonly HashSet<Thing> Set = new HashSet<Thing>();

			public bool Built;

			public int Epoch;
		}

		public static List<Thing> GetCandidates(Map map, WorkGiver_Scanner scanner)
		{
			var state = GetOrCreateState(map);
			if (!state.Built || state.Epoch != _epoch)
				Rebuild(map, state);

			var type = scanner.GetType();
			if (type == typeof(WorkGiver_Refuel))
				return state.NonTurrets;
			if (type == typeof(WorkGiver_Refuel_Turret))
				return state.Turrets;
			return state.All;
		}

		public static void Invalidate()
		{
			_epoch++;
		}

		public static void NotifyChanged(CompRefuelable comp)
		{
			var thing = comp.parent;
			if (thing == null || !thing.Spawned)
				return;
			ref var state = ref PrepatcherFields.RefuelCandidates(thing.Map);
			if (state == null)
				return;

			Add(state, thing);
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

		private static void Rebuild(Map map, State state)
		{
			state.All.Clear();
			state.NonTurrets.Clear();
			state.Turrets.Clear();
			state.Set.Clear();

			var refuelables = map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable);
			for (var i = 0; i < refuelables.Count; i++)
			{
				var thing = refuelables[i];
				if (thing.TryGetComp<CompRefuelable>() != null)
					Add(state, thing);
			}

			state.Built = true;
			state.Epoch = _epoch;
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
