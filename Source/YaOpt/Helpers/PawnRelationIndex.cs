using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Generates the initial relation sample with one weight evaluation per sampled pair.
	/// </summary>
	/// <remarks>
	/// Vanilla's <c>RandomElementByWeightWithDefault</c> asks the sampler for every sample's weight once
	/// for the default-weight check and again for the actual draw, and the draw itself repeats the sum.
	/// This implementation keeps the same sample order, random calls, default-weight formula, negative
	/// weight handling and worker calls, but stores the weights once. HAR's genderless relation prefix
	/// stays authoritative; unknown patches on <c>GeneratePawnRelations</c> or
	/// <see cref="PawnRelationWorker.GenerationChance"/> disable this path before it is installed.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnGenerationIndexCompat"/>
	internal static class PawnRelationIndex
	{
		private const int SampleCount = 40;

		private const string HarAlienRaceDefType = "AlienRace.ThingDef_AlienRace";

		private struct Sample
		{
			internal Pawn Pawn;

			internal PawnRelationDef Def;
		}

		private static Type _harAlienRaceDefType;

		/// <summary>
		/// Returns false when vanilla or Humanoid Alien Races must keep ownership of the call.
		/// </summary>
		internal static bool CanReplace(Pawn pawn)
		{
			if (!PawnGenerationIndexCompat.MasterEnabled || !PawnGenerationIndexCompat.RelationReady
				|| pawn?.RaceProps == null || !pawn.RaceProps.Humanlike)
			{
				return false;
			}

			if (pawn.RaceProps.hasGenders)
				return true;

			// HAR implements genderless races with its own prefix; replacing here would bypass it.
			if (_harAlienRaceDefType == null)
				_harAlienRaceDefType = AccessTools.TypeByName(HarAlienRaceDefType);
			return _harAlienRaceDefType == null
				|| !_harAlienRaceDefType.IsAssignableFrom(pawn.def.GetType());
		}

		/// <summary>
		/// Mirrors <c>PawnGenerator.GeneratePawnRelations</c> with cached sample weights.
		/// </summary>
		internal static void Generate(Pawn pawn, ref PawnGenerationRequest request)
		{
			if (!pawn.RaceProps.Humanlike)
				return;

			var all = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
			var count = 0;
			for (var i = 0; i < all.Count; i++)
			{
				if (all[i].def == pawn.def)
					count++;
			}

			if (count == 0)
				return;

			var pawns = new Pawn[count];
			var index = 0;
			var playerPawns = 0;
			for (var i = 0; i < all.Count; i++)
			{
				var other = all[i];
				if (other.def != pawn.def)
					continue;

				if (other.Discarded)
				{
					Log.Warning("Warning during generating pawn relations for " + pawn + ": Pawn " +
						other + " is discarded, yet he was yielded by PawnUtility. Discarding a pawn " +
						"means that he is no longer managed by anything.");
				}
				else if (other.Faction != null && other.Faction.IsPlayer)
				{
					playerPawns++;
				}

				pawns[index++] = other;
			}

			var defaultWeight = 45f + playerPawns * 2.7f;
			var localRequest = request;
			GenerateSampleSet(pawn, pawns, PawnGenerator.relationsGeneratableBlood, localRequest,
				defaultWeight, ref request);
			if (pawn.kindDef.generateInitialNonFamilyRelations)
			{
				GenerateSampleSet(pawn, pawns, PawnGenerator.relationsGeneratableNonblood, localRequest,
					defaultWeight, ref request);
			}
		}

		private static void GenerateSampleSet(Pawn pawn, Pawn[] pawns, PawnRelationDef[] relations,
			in PawnGenerationRequest localRequest, float defaultWeight,
			ref PawnGenerationRequest request)
		{
			if (relations.NullOrEmpty())
				return;

			var samples = GenerateSamples(pawns, relations, SampleCount);
			var selected = Select(pawn, samples, localRequest,
				defaultWeight * SampleCount / (pawns.Length * relations.Length));
			if (selected.Pawn != null)
				selected.Def.Worker.CreateRelation(pawn, selected.Pawn, ref request);
		}

		private static Sample[] GenerateSamples(Pawn[] pawns, PawnRelationDef[] relations, int count)
		{
			var samples = new Sample[count];
			for (var i = 0; i < count; i++)
			{
				samples[i] = new Sample
				{
					Pawn = pawns[Rand.Range(0, pawns.Length)],
					Def = relations[Rand.Range(0, relations.Length)]
				};
			}

			return samples;
		}

		private static Sample Select(Pawn pawn, Sample[] samples,
			in PawnGenerationRequest request, float defaultWeight)
		{
			if (defaultWeight < 0f)
			{
				Log.Error("Negative default value weight.");
				defaultWeight = 0f;
			}

			var weights = new float[samples.Length];
			var total = 0f;
			for (var i = 0; i < samples.Length; i++)
			{
				var sample = samples[i];
				var weight = sample.Def.generationChanceFactor
					* sample.Def.Worker.GenerationChance(pawn, sample.Pawn, request);
				if (weight < 0f)
				{
					Log.Error("Negative weight in selector: " + weight + " from " + sample.Pawn);
					weight = 0f;
				}

				weights[i] = weight;
				total += weight;
			}

			var totalWithDefault = defaultWeight + total;
			if (totalWithDefault <= 0f)
			{
				Log.Error("RandomElementByWeightWithDefault with totalWeight=" + totalWithDefault);
				return default;
			}

			if (Rand.Value < defaultWeight / totalWithDefault || total == 0f)
				return default;

			if (samples.Length == 1 && total > 0f)
				return samples[0];

			var roll = Rand.Value * total;
			var accumulated = 0f;
			for (var i = 0; i < samples.Length; i++)
			{
				var weight = weights[i];
				if (weight <= 0f)
					continue;

				accumulated += weight;
				if (accumulated >= roll)
					return samples[i];
			}

			return default;
		}
	}
}