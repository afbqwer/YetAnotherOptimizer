using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replacement bodies for the social opinion pipeline: <see cref="ThoughtHandler.TotalOpinionOffset"/>
	/// and <see cref="SituationalThoughtHandler"/>'s private <c>TryCreateSocialThought</c>.
	/// </summary>
	/// <remarks>
	/// Both vanilla methods pay the same work twice and neither is claimed by another mod, so they are
	/// installed as load-time replacements by <see cref="YaOpt.Prepatch.PrepatcherFreePatches"/>.
	/// Every entry point keeps a body-for-body vanilla fallback, so the option only removes duplicate
	/// work and never changes the result.
	/// </remarks>
	internal static class SocialOpinionHelper
	{
		/// <summary>
		/// Guards the static scratch lists below. <c>OpinionOffset()</c> is an extension point, so a
		/// mod override could re-enter the opinion pipeline; vanilla has the same hazard with its own
		/// static buffers and silently corrupts them. Re-entry is routed to the vanilla body, which
		/// uses separate buffers.
		/// </summary>
		private static bool _computing;

		private static readonly List<ISocialThought> TmpAll = new List<ISocialThought>(32);
		private static readonly List<ISocialThought> TmpRepresentatives = new List<ISocialThought>(8);
		private static readonly List<ISocialThought> TmpGroup = new List<ISocialThought>(16);
		private static readonly List<float> TmpGroupOffsets = new List<float>(16);
		private static readonly List<ISocialThought> TmpVanillaGroups = new List<ISocialThought>(8);

		private static readonly Comparison<ISocialThought> AgeComparison =
			(a, b) => ((Thought_Memory)a).age.CompareTo(((Thought_Memory)b).age);

		public static int TotalOpinionOffset(ThoughtHandler handler, Pawn otherPawn)
		{
			if (_computing)
				return VanillaTotalOpinionOffset(handler, otherPawn);

			_computing = true;
			try
			{
				// One scan for the whole call. Vanilla re-runs this inside every OpinionOffsetOfGroup,
				// which costs a memory scan, a cache lookup and a list copy per distinct group.
				handler.GetSocialThoughts(otherPawn, TmpAll);

				// Vanilla walks backwards and drops every element that groups with an earlier one, so
				// the first element of each group survives. GroupsWith is an equivalence relation
				// ((def, stage) with the stagesStack rule), so comparing against the survivors is
				// equivalent to comparing against all earlier elements.
				TmpRepresentatives.Clear();
				for (var i = 0; i < TmpAll.Count; i++)
				{
					var candidate = (Thought)TmpAll[i];
					var duplicate = false;
					for (var j = 0; j < TmpRepresentatives.Count; j++)
					{
						if (((Thought)TmpRepresentatives[j]).GroupsWith(candidate))
						{
							duplicate = true;
							break;
						}
					}
					if (!duplicate)
						TmpRepresentatives.Add(TmpAll[i]);
				}

				var total = 0;
				for (var i = 0; i < TmpRepresentatives.Count; i++)
					total += OpinionOffsetOfGroup(TmpRepresentatives[i], TmpAll);

				return total;
			}
			finally
			{
				TmpAll.Clear();
				TmpRepresentatives.Clear();
				TmpGroup.Clear();
				TmpGroupOffsets.Clear();
				_computing = false;
			}
		}

		public static int VanillaTotalOpinionOffset(ThoughtHandler handler, Pawn otherPawn)
		{
			handler.GetDistinctSocialThoughtGroups(otherPawn, TmpVanillaGroups);
			var num = 0;
			for (var i = 0; i < TmpVanillaGroups.Count; i++)
				num += handler.OpinionOffsetOfGroup(TmpVanillaGroups[i], otherPawn);
			TmpVanillaGroups.Clear();
			return num;
		}

		/// <summary>
		/// Same result as <see cref="ThoughtHandler.OpinionOffsetOfGroup"/>, but the members come from
		/// the caller's already-scanned list and <c>OpinionOffset()</c> runs once per member instead of
		/// twice (vanilla calls it again while summing, which doubles the cost of overrides such as
		/// <see cref="Thought_Incestuous"/>).
		/// </summary>
		private static int OpinionOffsetOfGroup(ISocialThought group, List<ISocialThought> all)
		{
			var groupThought = (Thought)group;
			TmpGroup.Clear();
			TmpGroupOffsets.Clear();

			for (var i = 0; i < all.Count; i++)
			{
				var member = all[i];
				if (!((Thought)member).GroupsWith(groupThought))
					continue;
				var offset = member.OpinionOffset();
				if (offset == 0f)
					continue;
				TmpGroup.Add(member);
				TmpGroupOffsets.Add(offset);
			}

			if (TmpGroup.Count == 0)
				return 0;

			var def = groupThought.def;
			var sorted = false;
			if (def.IsMemory && def.stackedEffectMultiplier != 1f)
			{
				// Identical comparator, identical input order: List.Sort produces the same permutation
				// vanilla produces, which matters because the multiplier accumulates in list order.
				TmpGroup.Sort(AgeComparison);
				sorted = true;
			}

			var sum = 0f;
			var multiplier = 1f;
			for (var i = 0; i < TmpGroup.Count; i++)
			{
				var offset = sorted ? TmpGroup[i].OpinionOffset() : TmpGroupOffsets[i];
				sum += offset * multiplier;
				multiplier *= ((Thought)TmpGroup[i]).def.stackedEffectMultiplier;
			}

			TmpGroup.Clear();
			TmpGroupOffsets.Clear();

			if (sum == 0f)
				return 0;
			if (sum > 0f)
				return Mathf.Max(Mathf.RoundToInt(sum), 1);
			return Mathf.Min(Mathf.RoundToInt(sum), -1);
		}

		public static Thought_SituationalSocial TryCreateSocialThought(SituationalThoughtHandler handler,
			ThoughtDef def, Pawn otherPawn)
		{
			var pawn = handler.pawn;
			Thought_SituationalSocial thought = null;
			try
			{
				// Vanilla evaluates this here and then again inside ThoughtWorker.FailState, which
				// CurrentSocialState calls. Calling CurrentSocialStateInternal directly skips that
				// duplicate; the FailState branch it skips only re-runs the check we just did and
				// would have returned null (letting CurrentSocialStateInternal run) anyway.
				if (!ThoughtUtility.CanGetThought(pawn, def))
					return null;

				var state = def.Worker.CurrentSocialStateInternal(pawn, otherPawn);

				// Reproduce ThoughtWorker.PostProcessedState, which CurrentSocialState applies and
				// CurrentSocialStateInternal does not. Dropping the outer check without this would
				// turn an inverted def's failed state into an active stage 0.
				if (def.invert)
					state = state.Active ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(0);

				if (!state.ActiveFor(def))
					return null;

				if (!def.socialTargetDevelopmentalStageFilter.HasAny(otherPawn.DevelopmentalStage))
					return null;

				if (def.ignoreSubhumans && otherPawn.IsSubhuman)
					return null;

				thought = (Thought_SituationalSocial)ThoughtMaker.MakeThought(def);
				thought.pawn = pawn;
				thought.otherPawn = otherPawn;
				if (def.Worker is ThoughtWorker_Precept_Social)
					thought.sourcePrecept = pawn.Ideo.GetFirstPreceptAllowingSituationalThought(def);

				// Kept as a full recalculate so the thought's stage, reason and active/inactive
				// transition notifications come from vanilla itself.
				thought.RecalculateState();
			}
			catch (Exception ex)
			{
				Log.Error("Exception while recalculating " + def?.ToString() + " thought state for pawn " +
					pawn?.ToString() + ": " + ex);
			}
			return thought;
		}

		public static Thought_SituationalSocial VanillaTryCreateSocialThought(SituationalThoughtHandler handler,
			ThoughtDef def, Pawn otherPawn)
		{
			var pawn = handler.pawn;
			Thought_SituationalSocial thought = null;
			try
			{
				if (!ThoughtUtility.CanGetThought(pawn, def))
					return null;

				if (!def.Worker.CurrentSocialState(pawn, otherPawn).ActiveFor(def))
					return null;

				if (!def.socialTargetDevelopmentalStageFilter.HasAny(otherPawn.DevelopmentalStage))
					return null;

				if (def.ignoreSubhumans && otherPawn.IsSubhuman)
					return null;

				thought = (Thought_SituationalSocial)ThoughtMaker.MakeThought(def);
				thought.pawn = pawn;
				thought.otherPawn = otherPawn;
				if (def.Worker is ThoughtWorker_Precept_Social)
					thought.sourcePrecept = pawn.Ideo.GetFirstPreceptAllowingSituationalThought(def);

				thought.RecalculateState();
			}
			catch (Exception ex)
			{
				Log.Error("Exception while recalculating " + def?.ToString() + " thought state for pawn " +
					pawn?.ToString() + ": " + ex);
			}
			return thought;
		}
	}
}
