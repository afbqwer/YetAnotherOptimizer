using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Death- and rescue-thought paths with narrower candidate scans.
	/// </summary>
	/// <remarks>
	/// The Died path visits pawns near the victim through the thing grid instead of scanning every
	/// living pawn on every map. Witnesses are deduplicated because a multi-cell pawn is listed in
	/// every cell it occupies. Relation thoughts use one materialized candidate list. General lost
	/// memories are still cleared from map, world and temporary pawns, while relation-specific
	/// cleanup avoids scanning every pawn for every relation definition.
	/// </remarks>
	internal static class DeathThoughtsHelper
	{
		private static readonly HashSet<Pawn> WitnessedPawns = new HashSet<Pawn>();

		private static readonly List<Pawn> RelatedPawns = new List<Pawn>(16);

		private static readonly List<ThoughtDef> LostRelationThoughtDefs =
			new List<ThoughtDef>(16);

		private static readonly List<ThoughtDef> DiedRelationThoughtDefs =
			new List<ThoughtDef>(16);

		/// <summary>
		/// Pawns that died while thoughts were being given, so their death memories may still exist.
		/// </summary>
		/// <remarks>
		/// <see cref="RemoveDiedThoughts"/> is called for every freshly spawned pawn. A pawn that never
		/// died cannot have death memories about itself, so the whole map scan is wasted work. This set
		/// records the few pawns that actually produced death thoughts; a save load marks everything dirty
		/// because the set does not survive serialization.
		/// </remarks>
		private static readonly HashSet<Pawn> DeathMemoryOwners = new HashSet<Pawn>();

		/// <summary>
		/// Pawns already cleaned once since the last cache clear. Used while the state is dirty
		/// after a save load so each pawn is scanned at most once per load.
		/// </summary>
		private static readonly HashSet<Pawn> DeathMemoryCleaned = new HashSet<Pawn>();

		private static bool _deathMemoryStateDirty = true;

		static DeathThoughtsHelper()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(MarkDeathMemoryStateDirty);
		}

		private static void MarkDeathMemoryStateDirty()
		{
			DeathMemoryOwners.Clear();
			DeathMemoryCleaned.Clear();
			_deathMemoryStateDirty = true;
		}

		/// <summary>
		/// Returns true when <paramref name="pawn"/> may still have death memories about it.
		/// </summary>
		internal static bool MayHaveDeathMemories(Pawn pawn)
		{
			if (DeathMemoryOwners.Contains(pawn))
				return true;
			return _deathMemoryStateDirty && !DeathMemoryCleaned.Contains(pawn);
		}

		/// <summary>
		/// Records that this pawn's death memories were cleared.
		/// </summary>
		/// <remarks>
		/// The global dirty flag is intentionally kept until the next cache clear. After a save load
		/// every pawn may carry restored death memories, so relaxing the flag on the first cleanup
		/// could skip a later revived pawn in the same batch.
		/// </remarks>
		internal static void ClearDeathMemoryOwner(Pawn pawn)
		{
			DeathMemoryOwners.Remove(pawn);
			if (_deathMemoryStateDirty)
				DeathMemoryCleaned.Add(pawn);
		}

		public static void TryGiveDiedThoughts(Pawn victim, DamageInfo? dinfo)
		{
			try
			{
				if (PawnGenerator.IsBeingGenerated(victim) ||
					Current.ProgramState != ProgramState.Playing ||
					victim.wasLeftBehindStartingPawn)
					return;

				DeathMemoryOwners.Add(victim);
				WitnessedPawns.Clear();
				if (victim.RaceProps.Humanlike)
					AddHumanlikeDiedThoughts(victim, dinfo);
				if (victim.relations != null && victim.relations.everSeenByPlayer)
					AddRelationDiedThoughts(victim, dinfo);

				if ((dinfo.HasValue && dinfo.Value.Def.execution) || !victim.IsPrisonerOfColony)
					return;

				var responsible = FindResponsibleColonist(victim, dinfo);
				if (!victim.guilt.IsGuilty && !victim.InAggroMentalState)
				{
					Find.HistoryEventsManager.RecordEvent(
						new HistoryEvent(HistoryEventDefOf.InnocentPrisonerDied,
							responsible.Named(HistoryEventArgsNames.Doer)));
				}
				else
				{
					Find.HistoryEventsManager.RecordEvent(
						new HistoryEvent(HistoryEventDefOf.GuiltyPrisonerDied,
							responsible.Named(HistoryEventArgsNames.Doer)));
				}
				Find.HistoryEventsManager.RecordEvent(
					new HistoryEvent(HistoryEventDefOf.PrisonerDied,
						responsible.Named(HistoryEventArgsNames.Doer)));
			}
			catch (Exception ex)
			{
				Log.Error("Could not give thoughts: " + ex);
			}
			finally
			{
				WitnessedPawns.Clear();
				RelatedPawns.Clear();
			}
		}

		/// <summary>
		/// Clears every living pawn's memories that record <paramref name="pawn"/>'s death.
		/// </summary>
		/// <remarks>
		/// Mirrors vanilla but materialises the gender-specific died thought defs once per call
		/// instead of recomputing them for every observer. The caller has already checked that this
		/// pawn may own death memories.
		/// </remarks>
		public static void RemoveDiedThoughts(Pawn pawn)
		{
			try
			{
				FillDiedRelationThoughtDefs(pawn);
				var alivePawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
				for (var i = 0; i < alivePawns.Count; i++)
				{
					var otherPawn = alivePawns[i];
					if (otherPawn == pawn || otherPawn.needs == null || otherPawn.needs.mood == null)
						continue;

					var memories = otherPawn.needs.mood.thoughts.memories;
					memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.KnowColonistDied, pawn);
					memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.KnowPrisonerDiedInnocent, pawn);
					memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.PawnWithGoodOpinionDied, pawn);
					memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.PawnWithBadOpinionDied, pawn);
					if (ModsConfig.BiotechActive)
						memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.Stillbirth, pawn);

					for (var j = 0; j < DiedRelationThoughtDefs.Count; j++)
						memories.RemoveMemoriesOfDefWhereOtherPawnIs(DiedRelationThoughtDefs[j], pawn);
				}
			}
			finally
			{
				DiedRelationThoughtDefs.Clear();
			}
		}

		private static void FillDiedRelationThoughtDefs(Pawn pawn)
		{
			DiedRelationThoughtDefs.Clear();
			var relationDefs = DefDatabase<PawnRelationDef>.AllDefsListForReading;
			for (var i = 0; i < relationDefs.Count; i++)
			{
				var thoughtDef = relationDefs[i].GetGenderSpecificDiedThought(pawn);
				if (thoughtDef != null)
					DiedRelationThoughtDefs.Add(thoughtDef);
			}
		}

		public static void RemoveLostThoughts(Pawn pawn)
		{
			RemoveGeneralLostThoughts(pawn);

			var relations = pawn.relations;
			if (relations == null || !relations.everSeenByPlayer)
				return;

			try
			{
				FillLostRelationThoughtDefs(pawn);
				RemoveRelationLostThoughts(pawn, relations);
			}
			finally
			{
				LostRelationThoughtDefs.Clear();
			}
		}

		public static void RemoveResuedRelativeThought(Pawn pawn)
		{
			var alivePawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
			for (var i = 0; i < alivePawns.Count; i++)
			{
				var otherPawn = alivePawns[i];
				if (otherPawn == pawn || otherPawn.needs == null || otherPawn.needs.mood == null)
					continue;

				otherPawn.needs.mood.thoughts.memories.RemoveMemoriesOfDefWhereOtherPawnIs(
					ThoughtDefOf.RescuedRelative, pawn);
			}
		}

		private static void AddHumanlikeDiedThoughts(Pawn victim, DamageInfo? dinfo)
		{
			var isExecution = dinfo.HasValue && dinfo.Value.Def.execution;
			if (dinfo.HasValue && dinfo.Value.Def.ExternalViolenceFor(victim) &&
				dinfo.Value.Instigator is Pawn instigator)
			{
				if (!instigator.Dead && instigator.needs != null &&
					instigator.needs.mood != null && instigator.story != null &&
					instigator != victim && PawnUtility.ShouldGetThoughtAbout(instigator, victim))
				{
					AddThought(ThoughtDefOf.KilledHumanlikeBloodlust, instigator);

					if (victim.HostileTo(instigator) && victim.Faction != null &&
						PawnUtility.IsFactionLeader(victim) &&
						victim.Faction.HostileTo(instigator.Faction))
					{
						AddThought(ThoughtDefOf.DefeatedHostileFactionLeader, instigator, victim);
					}

					if (ModsConfig.BiotechActive && !victim.DevelopmentalStage.Adult() &&
						instigator.DevelopmentalStage.Adult())
					{
						AddThought(ThoughtDefOf.KilledChild, instigator, victim);
					}
				}
			}

			if (isExecution)
				return;

			if (victim.IsCaravanMember())
				AddWitnessedDeathThoughtsForCaravan(victim);
			else if (victim.Spawned)
				AddWitnessedDeathThoughtsForMap(victim);

			if (victim.Faction != Faction.OfPlayer ||
				victim.HostFaction == Faction.OfPlayer ||
				victim.IsQuestLodger() ||
				victim.IsSubhuman ||
				victim.IsSlave)
				return;

			var playerPawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
			for (var i = 0; i < playerPawns.Count; i++)
			{
				var pawn = playerPawns[i];
				if (pawn == victim || pawn.needs == null || pawn.needs.mood == null ||
					IsExcludedSocialFightWitness(pawn, victim) ||
					WasHandledAsWitness(pawn, victim))
					continue;

				AddThought(ThoughtDefOf.KnowColonistDied, pawn, victim);
			}
		}

		private static void AddWitnessedDeathThoughtsForCaravan(Pawn victim)
		{
			var caravan = victim.GetCaravan();
			if (caravan == null)
				return;

			var pawns = caravan.PawnsListForReading;
			for (var i = 0; i < pawns.Count; i++)
			{
				var pawn = pawns[i];
				if (pawn == null || !WitnessedPawns.Add(pawn))
					continue;
				TryAddWitnessedDeathThoughts(pawn, victim);
			}
		}

		private static void AddWitnessedDeathThoughtsForMap(Pawn victim)
		{
			var map = victim.Map;
			var cellCount = GenRadial.NumCellsInRadius(12f);
			for (var i = 0; i < cellCount; i++)
			{
				var cell = victim.Position + GenRadial.RadialPattern[i];
				if (!cell.InBounds(map))
					continue;

				var things = map.thingGrid.ThingsListAtFast(cell);
				for (var j = 0; j < things.Count; j++)
				{
					if (!(things[j] is Pawn pawn) || !WitnessedPawns.Add(pawn))
						continue;
					TryAddWitnessedDeathThoughts(pawn, victim);
				}
			}
		}

		private static void TryAddWitnessedDeathThoughts(Pawn pawn, Pawn victim)
		{
			if (pawn == victim || pawn.needs == null || pawn.needs.mood == null ||
				!PawnUtility.ShouldGetThoughtAbout(pawn, victim) ||
				IsExcludedSocialFightWitness(pawn, victim) ||
				!ThoughtUtility.Witnessed(pawn, victim))
				return;

			var questLodger = pawn.Faction == Faction.OfPlayer && victim.IsQuestLodger();
			if (pawn.Faction == victim.Faction && !questLodger && !victim.IsSlave)
			{
				AddThought(ThoughtDefOf.WitnessedDeathAlly, pawn);
			}
			else if (victim.Faction == null || !victim.Faction.HostileTo(pawn.Faction) ||
					 questLodger || victim.IsSlave)
			{
				AddThought(ThoughtDefOf.WitnessedDeathNonAlly, pawn);
			}

			if (IsFamilyByBlood(pawn, victim))
				AddThought(ThoughtDefOf.WitnessedDeathFamily, pawn);

			AddThought(ThoughtDefOf.WitnessedDeathBloodlust, pawn);
		}

		private static bool IsFamilyByBlood(Pawn pawn, Pawn victim)
		{
			foreach (var relative in pawn.relations.FamilyByBlood)
			{
				if (relative == victim)
					return true;
			}
			return false;
		}

		private static void AddRelationDiedThoughts(Pawn victim, DamageInfo? dinfo)
		{
			FillRelatedPawns(victim);
			for (var i = 0; i < RelatedPawns.Count; i++)
			{
				var pawn = RelatedPawns[i];
				if (!PawnUtility.ShouldGetThoughtAbout(pawn, victim))
					continue;

				var relation = pawn.GetMostImportantRelation(victim);
				var thought = relation == null
					? null
					: relation.GetGenderSpecificThought(victim, PawnDiedOrDownedThoughtsKind.Died);
				if (thought != null)
					AddThought(thought, pawn, victim);
			}

			if (dinfo.HasValue && dinfo.Value.Instigator is Pawn instigator && instigator != victim)
			{
				for (var i = 0; i < RelatedPawns.Count; i++)
				{
					var pawn = RelatedPawns[i];
					if (pawn == instigator || !PawnUtility.ShouldGetThoughtAbout(pawn, victim))
						continue;

					var relation = pawn.GetMostImportantRelation(victim);
					var killedThought = relation == null
						? null
						: relation.GetGenderSpecificKilledThought(victim);
					if (killedThought != null)
						AddThought(killedThought, pawn, instigator);

					if (!pawn.RaceProps.IsFlesh)
						continue;

					var opinion = pawn.relations.OpinionOf(victim);
					if (opinion >= 20)
					{
						AddThought(ThoughtDefOf.KilledMyFriend, pawn, instigator, 1f,
							victim.relations.GetFriendDiedThoughtPowerFactor(opinion));
					}
					else if (opinion <= -20)
					{
						AddThought(ThoughtDefOf.KilledMyRival, pawn, instigator, 1f,
							victim.relations.GetRivalDiedThoughtPowerFactor(opinion));
					}
				}
			}

			if (!victim.RaceProps.Humanlike)
				return;

			var alivePawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
			for (var i = 0; i < alivePawns.Count; i++)
			{
				var pawn = alivePawns[i];
				if (pawn == victim || pawn.needs == null || pawn.needs.mood == null ||
					!pawn.RaceProps.IsFlesh ||
					!PawnUtility.ShouldGetThoughtAbout(pawn, victim))
					continue;

				var opinion = pawn.relations.OpinionOf(victim);
				if (opinion >= 20)
				{
					AddThought(ThoughtDefOf.PawnWithGoodOpinionDied, pawn, victim,
						victim.relations.GetFriendDiedThoughtPowerFactor(opinion));
				}
				else if (opinion <= -20)
				{
					AddThought(ThoughtDefOf.PawnWithBadOpinionDied, pawn, victim,
						victim.relations.GetRivalDiedThoughtPowerFactor(opinion));
				}
			}
		}

		private static void FillRelatedPawns(Pawn victim)
		{
			RelatedPawns.Clear();
			foreach (var pawn in victim.relations.PotentiallyRelatedPawns)
			{
				if (pawn != null && pawn.needs != null && pawn.needs.mood != null)
					RelatedPawns.Add(pawn);
			}
		}

		private static bool IsExcludedSocialFightWitness(Pawn pawn, Pawn victim)
		{
			return pawn.MentalStateDef == MentalStateDefOf.SocialFighting &&
				   ((MentalState_SocialFighting)pawn.MentalState).otherPawn == victim;
		}

		private static bool WasHandledAsWitness(Pawn pawn, Pawn victim)
		{
			if (pawn == victim || pawn.needs == null || pawn.needs.mood == null ||
				IsExcludedSocialFightWitness(pawn, victim))
				return false;

			if (victim.IsCaravanMember())
				return pawn.GetCaravan() == victim.GetCaravan() &&
					   ThoughtUtility.Witnessed(pawn, victim);

			return victim.Spawned && pawn.Spawned && pawn.Map == victim.Map &&
				   ThoughtUtility.Witnessed(pawn, victim);
		}

		private static void AddThought(ThoughtDef thoughtDef, Pawn addTo, Pawn otherPawn = null,
			float moodPowerFactor = 1f, float opinionOffsetFactor = 1f)
		{
			new IndividualThoughtToAdd(thoughtDef, addTo, otherPawn, moodPowerFactor,
				opinionOffsetFactor).Add();
		}

		private static Pawn FindResponsibleColonist(Pawn victim, DamageInfo? dinfo)
		{
			if (dinfo.HasValue && dinfo.Value.Instigator is Pawn instigator && instigator.IsColonist)
				return instigator;

			if (victim.Spawned)
			{
				var freeColonists = victim.Map.mapPawns.FreeColonistsSpawned;
				Pawn closestStanding = null;
				var closestStandingDistance = int.MaxValue;
				Pawn closest = null;
				var closestDistance = int.MaxValue;

				for (var i = 0; i < freeColonists.Count; i++)
				{
					var pawn = freeColonists[i];
					var distance = pawn.Position.DistanceToSquared(victim.Position);
					if (distance < closestDistance)
					{
						closestDistance = distance;
						closest = pawn;
					}
					if (pawn.Downed || distance >= closestStandingDistance)
						continue;
					closestStandingDistance = distance;
					closestStanding = pawn;
				}

				if (closestStanding != null)
					return closestStanding;
				if (closest != null)
					return closest;
			}

			var worldColonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
			return worldColonists.Count > 0 ? worldColonists[0] : null;
		}

		private static void RemoveGeneralLostThoughts(Pawn pawn)
		{
			var alivePawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
			for (var i = 0; i < alivePawns.Count; i++)
			{
				var otherPawn = alivePawns[i];
				if (otherPawn == pawn || otherPawn.needs == null || otherPawn.needs.mood == null)
					continue;

				var memories = otherPawn.needs.mood.thoughts.memories;
				memories.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.ColonistLost, pawn);
				memories.RemoveMemoriesOfDefWhereOtherPawnIs(
					ThoughtDefOf.PawnWithGoodOpinionLost, pawn);
				memories.RemoveMemoriesOfDefWhereOtherPawnIs(
					ThoughtDefOf.PawnWithBadOpinionLost, pawn);
			}
		}

		private static void FillLostRelationThoughtDefs(Pawn pawn)
		{
			LostRelationThoughtDefs.Clear();
			var relationDefs = DefDatabase<PawnRelationDef>.AllDefsListForReading;
			for (var i = 0; i < relationDefs.Count; i++)
			{
				var thoughtDef = relationDefs[i].GetGenderSpecificLostThought(pawn);
				if (thoughtDef != null)
					LostRelationThoughtDefs.Add(thoughtDef);
			}
		}

		private static void RemoveRelationLostThoughts(Pawn pawn, Pawn_RelationsTracker relations)
		{
			foreach (var related in relations.PotentiallyRelatedPawns)
			{
				if (related == null || related == pawn || related.Dead ||
					related.needs == null || related.needs.mood == null)
					continue;

				var memories = related.needs.mood.thoughts.memories;
				for (var i = 0; i < LostRelationThoughtDefs.Count; i++)
				{
					memories.RemoveMemoriesOfDefWhereOtherPawnIs(LostRelationThoughtDefs[i], pawn);
				}
			}
		}
	}
}