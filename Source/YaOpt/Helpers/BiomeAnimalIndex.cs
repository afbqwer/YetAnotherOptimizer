using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	///     Serves <see cref="BiomeDef.AllWildAnimals" /> from a per-biome list built in one pass.
	/// </summary>
	/// <remarks>
	///     Vanilla enumerates every PawnKindDef on each access, and builds the biome's commonality
	///     dictionaries with <c>Dictionary.Add</c> on first use. A PawnKindDef that is listed both
	///     in the biome's wildAnimals and in its own wildBiomes makes that Add throw, leaving the
	///     dictionary half filled for the rest of the session. The dictionaries are therefore
	///     prebuilt here with duplicate-safe merging, and the composed lists are served from the
	///     index.
	/// </remarks>
	internal static class BiomeAnimalIndex
	{
		/// <summary>Biome -> the exact list vanilla's AllWildAnimals yields, in database order.</summary>
		internal static Dictionary<BiomeDef, ReadOnlyCollection<PawnKindDef>> AllWildAnimalsByBiome;

		internal static void Invalidate()
		{
			AllWildAnimalsByBiome = null;
		}

		internal static void Build()
		{
			AllWildAnimalsByBiome = null;

			var biomes = DefDatabase<BiomeDef>.AllDefsListForReading;
			var pawnKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
			if (biomes.Count == 0 || pawnKinds.Count == 0)
				return;

			try
			{
				var stopwatch = Stopwatch.StartNew();
				var biotech = ModsConfig.BiotechActive;
				var odyssey = ModsConfig.OdysseyActive;
				var normal = new Dictionary<BiomeDef, Dictionary<PawnKindDef, float>>(biomes.Count);
				var pollution = new Dictionary<BiomeDef, Dictionary<PawnKindDef, float>>(biomes.Count);
				var coastal = new Dictionary<BiomeDef, Dictionary<PawnKindDef, float>>(biomes.Count);
				var duplicates = 0;
				string firstDuplicate = null;

				for (var i = 0; i < biomes.Count; i++)
				{
					var biome = biomes[i];
					var normalForBiome = new Dictionary<PawnKindDef, float>();
					MergeRecords(normalForBiome, biome.wildAnimals, biome, "wildAnimals",
						ref duplicates, ref firstDuplicate);
					normal[biome] = normalForBiome;

					if (biotech)
					{
						var pollutionForBiome = new Dictionary<PawnKindDef, float>();
						MergeRecords(pollutionForBiome, biome.pollutionWildAnimals, biome,
							"pollutionWildAnimals", ref duplicates, ref firstDuplicate);
						pollution[biome] = pollutionForBiome;
					}

					if (odyssey)
					{
						var coastalForBiome = new Dictionary<PawnKindDef, float>();
						MergeRecords(coastalForBiome, biome.coastalWildAnimals, biome, "coastalWildAnimals",
							ref duplicates, ref firstDuplicate);
						coastal[biome] = coastalForBiome;
					}
				}

				// Same source as vanilla's second pass: each PawnKindDef's own wildBiomes records.
				for (var i = 0; i < pawnKinds.Count; i++)
				{
					var kind = pawnKinds[i];
					var records = kind.RaceProps?.wildBiomes;
					if (records == null)
						continue;

					for (var j = 0; j < records.Count; j++)
					{
						var record = records[j];
						if (record == null)
							continue;

						var biome = record.biome;
						if (biome == null || !normal.TryGetValue(biome, out var dict))
							continue;

						if (!dict.TryAdd(kind, record.commonality))
						{
							// Vanilla's Dictionary.Add would throw here and leave the biome's cache
							// half filled. Merge instead, keeping the higher commonality so the
							// composed list and the commonality lookups stay consistent.
							if (record.commonality > dict[kind])
								dict[kind] = record.commonality;

							duplicates++;
							if (firstDuplicate == null)
								firstDuplicate = $"{kind.defName} in {biome.defName} (wildBiomes)";
						}
					}
				}

				// Vanilla builds the list by filtering all PawnKindDefs in database order.
				var byBiome = new Dictionary<BiomeDef, ReadOnlyCollection<PawnKindDef>>(biomes.Count);
				var totalAnimals = 0;
				for (var i = 0; i < biomes.Count; i++)
				{
					var biome = biomes[i];
					var positives = new HashSet<PawnKindDef>();
					AddPositive(normal[biome], positives);
					if (biotech)
						AddPositive(pollution[biome], positives);

					if (odyssey)
						AddPositive(coastal[biome], positives);

					var list = new List<PawnKindDef>(positives.Count);
					for (var j = 0; j < pawnKinds.Count; j++)
					{
						if (positives.Contains(pawnKinds[j]))
							list.Add(pawnKinds[j]);
					}

					byBiome[biome] = list.AsReadOnly();
					totalAnimals += list.Count;
				}

				// Assigning the dictionaries makes CommonalityOfAnimal skip its throwing lazy build.
				for (var i = 0; i < biomes.Count; i++)
				{
					var biome = biomes[i];
					biome.cachedAnimalCommonalities = normal[biome];
					if (biotech)
						biome.cachedPollutionAnimalCommonalities = pollution[biome];

					if (odyssey)
						biome.cachedCoastalAnimalCommonalities = coastal[biome];
				}

				AllWildAnimalsByBiome = byBiome;
				YaOptMod.Log($"Biome animal index: {byBiome.Count} biomes and {totalAnimals} wild animal " +
							 $"entries in {stopwatch.ElapsedMilliseconds} ms.");

				if (duplicates > 0)
				{
					YaOptMod.Warning($"Merged {duplicates} duplicate biome animal record(s); first: " +
									 $"{firstDuplicate}. Vanilla would have thrown and left the biome cache " +
									 "incomplete; the higher commonality was kept.");
				}
			}
			catch (Exception exception)
			{
				AllWildAnimalsByBiome = null;
				YaOptMod.Error($"Failed to build the biome animal index, falling back to vanilla:\n{exception}");
			}
		}

		private static void MergeRecords(Dictionary<PawnKindDef, float> dict, List<BiomeAnimalRecord> records,
			BiomeDef biome, string source, ref int duplicates, ref string firstDuplicate)
		{
			if (records == null)
				return;

			for (var i = 0; i < records.Count; i++)
			{
				var record = records[i];
				if (record == null)
					continue;

				var animal = record.animal;
				if (animal == null)
					continue;

				if (dict.TryGetValue(animal, out var existing))
				{
					if (record.commonality > existing)
						dict[animal] = record.commonality;

					duplicates++;
					if (firstDuplicate == null)
						firstDuplicate = $"{animal.defName} in {biome.defName} ({source})";
				}
				else
				{
					dict[animal] = record.commonality;
				}
			}
		}

		private static void AddPositive(Dictionary<PawnKindDef, float> dict, HashSet<PawnKindDef> positives)
		{
			foreach (var pair in dict)
			{
				if (pair.Value > 0f)
					positives.Add(pair.Key);
			}
		}
	}
}