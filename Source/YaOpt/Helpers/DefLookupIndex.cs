using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using UnlockedHeader = RimWorld.ResearchPrerequisitesUtility.UnlockedHeader;

namespace YaOpt.Helpers
{
	/// <summary>
	///     Cached lookups for the database-scanning properties used by the recipes and research UI.
	/// </summary>
	/// <remarks>
	///     Keep this type free of static initialisers: reading the fields from the patches must
	///     never trigger a build. The build is owned by <see cref="Build" />, which runs once after
	///     all defs are loaded and before the first <c>[StaticConstructorOnStartup]</c>.
	/// </remarks>
	internal static class DefLookupIndex
	{
		/// <summary>Def -> recipes, i.e. what ThingDef.AllRecipes computes.</summary>
		internal static Dictionary<ThingDef, List<RecipeDef>> ByDef;

		/// <summary>Recipe -> users, i.e. what RecipeDef.AllRecipeUsers computes.</summary>
		internal static Dictionary<RecipeDef, ReadOnlyCollection<ThingDef>> UsersByRecipe;

		/// <summary>Def -> projects unlocking it, i.e. what ComputeResearchPrerequisites computes.</summary>
		internal static Dictionary<Def, List<ResearchProjectDef>> PrerequisitesByDef;

		/// <summary>Project -> defs it unlocks, i.e. what ResearchProjectDef.UnlockedDefs computes.</summary>
		internal static Dictionary<ResearchProjectDef, List<Def>> DefsByProject;

		/// <summary>
		///     Project -> the grouped "Unlocks" panel entries, i.e. what
		///     ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites computes.
		/// </summary>
		internal static Dictionary<ResearchProjectDef, List<Pair<UnlockedHeader, List<Def>>>>
			UnlockedGroupsByProject;

		private static bool built;

		internal static void Invalidate()
		{
			built = false;
			ByDef = null;
			UsersByRecipe = null;
			PrerequisitesByDef = null;
			DefsByProject = null;
			UnlockedGroupsByProject = null;
		}

		internal static void Build(string source)
		{
			if (built)
				return;

			var thingDefs = DefDatabase<ThingDef>.AllDefsListForReading;
			var recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
			if (thingDefs.Count == 0 || recipes.Count == 0)
			{
				// Never publish indexes built from a partial database, because they would overwrite
				// the vanilla caches with wrong lists.
				Invalidate();
				return;
			}

			// A rebuild starts from scratch: if it fails, the patches must fall back to vanilla
			// instead of serving stale or partially filled indexes.
			Invalidate();

			try
			{
				var stopwatch = Stopwatch.StartNew();
				var recipeIndex = BuildRecipeIndexes(thingDefs, recipes);
				ByDef = recipeIndex.ByDef;
				UsersByRecipe = Wrap(recipeIndex.UsersByRecipe);

				var research = BuildResearchIndexes(thingDefs, recipes, recipeIndex.UsersByRecipe);
				PrerequisitesByDef = research.PrerequisitesByDef;
				DefsByProject = research.DefsByProject;
				UnlockedGroupsByProject = research.Groups;

				built = true;
				YaOptMod.Log($"Def lookup index: {ByDef.Count} recipe users, {recipeIndex.UsersByRecipe.Count} recipes " +
							 $"and {DefsByProject.Count} research projects in {stopwatch.ElapsedMilliseconds} ms " +
							 $"(triggered by {source}).");
			}
			catch (Exception exception)
			{
				// Whatever was published before the failure stays valid; the rest falls back to vanilla.
				// built stays false, so a later hook can retry against the same defs.
				YaOptMod.Error($"Failed to build the def lookup index, falling back to vanilla:\n{exception}");
			}
		}

		private static (Dictionary<ThingDef, List<RecipeDef>> ByDef,
			Dictionary<RecipeDef, List<ThingDef>> UsersByRecipe) BuildRecipeIndexes(
				List<ThingDef> thingDefs, List<RecipeDef> recipes)
		{
			var index = new Dictionary<ThingDef, List<RecipeDef>>();
			var users = new HashSet<ThingDef>();

			// Vanilla appends recipeUsers matches after the def's own recipes; keep that order.
			foreach (var recipe in recipes)
			{
				if (recipe.recipeUsers == null)
					continue;

				if (recipe.recipeUsers.Count == 1)
				{
					AddUser(recipe, recipe.recipeUsers[0]);
					continue;
				}

				// Vanilla adds a recipe once per def (recipeUsers.Contains), so duplicates in the
				// list must not produce duplicate entries here either.
				users.Clear();
				for (var i = 0; i < recipe.recipeUsers.Count; i++)
				{
					if (users.Add(recipe.recipeUsers[i]))
						AddUser(recipe, recipe.recipeUsers[i]);
				}
			}

			// ThingDef.recipes comes first in vanilla and is not deduplicated against recipeUsers.
			foreach (var def in thingDefs)
			{
				var own = def.recipes;
				if (own == null || own.Count == 0)
					continue;

				if (!index.TryGetValue(def, out var list))
					index[def] = list = new List<RecipeDef>();

				list.InsertRange(0, own);
			}

			// RecipeDef.AllRecipeUsers has no cache in vanilla; it yields recipeUsers and then scans
			// the whole ThingDef database on every access. Cache the composed result instead.
			var usersByRecipeLists = new Dictionary<RecipeDef, List<ThingDef>>(recipes.Count);
			foreach (var recipe in recipes)
			{
				var list = new List<ThingDef>();
				if (recipe.recipeUsers != null)
				{
					// Vanilla yields every entry, duplicates included.
					list.AddRange(recipe.recipeUsers);
				}

				usersByRecipeLists[recipe] = list;
			}

			var usedRecipes = new HashSet<RecipeDef>();
			foreach (var def in thingDefs)
			{
				var own = def.recipes;
				if (own == null || own.Count == 0)
					continue;

				if (own.Count == 1)
				{
					AddDefUser(own[0], def);
					continue;
				}

				// Vanilla uses Contains here, so a recipe repeated in def.recipes yields the def once.
				usedRecipes.Clear();
				for (var i = 0; i < own.Count; i++)
				{
					if (usedRecipes.Add(own[i]))
						AddDefUser(own[i], def);
				}
			}

			return (index, usersByRecipeLists);

			void AddUser(RecipeDef recipe, ThingDef def)
			{
				if (def == null)
					return;

				if (!index.TryGetValue(def, out var list))
					index[def] = list = new List<RecipeDef>();

				list.Add(recipe);
			}

			void AddDefUser(RecipeDef recipe, ThingDef def)
			{
				if (recipe == null)
					return;

				if (!usersByRecipeLists.TryGetValue(recipe, out var list))
					usersByRecipeLists[recipe] = list = new List<ThingDef>();

				list.Add(def);
			}
		}

		private static (
			Dictionary<Def, List<ResearchProjectDef>> PrerequisitesByDef,
			Dictionary<ResearchProjectDef, List<Def>> DefsByProject,
			Dictionary<ResearchProjectDef, List<Pair<UnlockedHeader, List<Def>>>> Groups) BuildResearchIndexes(
				List<ThingDef> thingDefs, List<RecipeDef> recipes,
				Dictionary<RecipeDef, List<ThingDef>> usersByRecipe)
		{
			var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
			var byProject = new Dictionary<ResearchProjectDef, List<Def>>(projects.Count);
			foreach (var project in projects)
				byProject[project] = new List<Def>();

			// Mirrors ResearchProjectDef.UnlockedDefs source by source, in the same concatenation order,
			// so the final label sort also matches vanilla when labels are equal.
			foreach (var recipe in recipes)
			{
				AddProducts(recipe, recipe.researchPrerequisite);
				if (recipe.researchPrerequisites != null)
				{
					foreach (var project in recipe.researchPrerequisites)
						AddProducts(recipe, project);
				}
			}

			foreach (var thing in thingDefs)
			{
				if (thing.researchPrerequisites == null)
					continue;

				foreach (var project in thing.researchPrerequisites)
					Add(project, thing);
			}

			foreach (var thing in thingDefs)
			{
				var sow = thing.plant?.sowResearchPrerequisites;
				if (sow == null)
					continue;

				foreach (var project in sow)
					Add(project, thing);
			}

			foreach (var terrain in DefDatabase<TerrainDef>.AllDefsListForReading)
			{
				if (terrain.researchPrerequisites == null)
					continue;

				foreach (var project in terrain.researchPrerequisites)
					Add(project, terrain);
			}

			// Vanilla only checks the list field for surgery recipes, not researchPrerequisite.
			foreach (var recipe in recipes)
			{
				if (recipe.researchPrerequisites == null || !IsSurgery(recipe, usersByRecipe))
					continue;

				foreach (var project in recipe.researchPrerequisites)
					Add(project, recipe);
			}

			foreach (var ritual in DefDatabase<PsychicRitualDef>.AllDefsListForReading)
				Add(ritual.researchPrerequisite, ritual);

			// Vanilla sorts the concatenation by label and then deduplicates. Removing duplicates before
			// a stable sort yields the same order, because duplicates share the same label.
			var defsByProject = new Dictionary<ResearchProjectDef, List<Def>>(byProject.Count);
			foreach (var pair in byProject)
				defsByProject[pair.Key] = pair.Value.Distinct().OrderBy(def => def.label).ToList();

			// Same structure ComputeResearchPrerequisites builds, in project database order.
			var prerequisitesByDef = new Dictionary<Def, List<ResearchProjectDef>>();
			foreach (var project in projects)
			{
				var defs = defsByProject[project];
				for (var i = 0; i < defs.Count; i++)
				{
					var def = defs[i];
					if (prerequisitesByDef.TryGetValue(def, out var list))
					{
						list.Add(project);
					}
					else
					{
						prerequisitesByDef[def] = new List<ResearchProjectDef> { project };
					}
				}
			}

			// Same LINQ as UnlockedDefsGroupedByPrerequisites, precomputed for every project.
			var groups = new Dictionary<ResearchProjectDef, List<Pair<UnlockedHeader, List<Def>>>>(projects.Count);
			foreach (var project in projects)
			{
				var defs = defsByProject[project];
				var pairs = new List<Pair<Def, UnlockedHeader>>(defs.Count);
				for (var i = 0; i < defs.Count; i++)
				{
					var def = defs[i];
					var unlockedBy = new List<ResearchProjectDef>(prerequisitesByDef[def].Count);
					foreach (var other in prerequisitesByDef[def])
					{
						if (other != project)
							unlockedBy.Add(other);
					}

					pairs.Add(new Pair<Def, UnlockedHeader>(def, new UnlockedHeader(unlockedBy)));
				}

				groups[project] = pairs
					.OrderBy(pair => pair.Second.unlockedBy.Count)
					.GroupBy(pair => pair.Second, pair => pair.First)
					.Select(grouped => new Pair<UnlockedHeader, List<Def>>(grouped.Key, grouped.ToList()))
					.ToList();
			}

			return (prerequisitesByDef, defsByProject, groups);

			void Add(ResearchProjectDef project, Def def)
			{
				if (project == null || def == null)
					return;

				if (!byProject.TryGetValue(project, out var list))
				{
					// A project created after the database snapshot: keep it working instead of dropping
					// its defs; the patch simply will not serve a cached group for it.
					byProject[project] = list = new List<Def>();
				}

				list.Add(def);
			}

			void AddProducts(RecipeDef recipe, ResearchProjectDef project)
			{
				if (project == null || recipe.products == null)
					return;

				for (var i = 0; i < recipe.products.Count; i++)
					Add(project, recipe.products[i]?.thingDef);
			}
		}

		/// <summary>
		///     Same rule as RecipeDef.IsSurgery, but based on the already-built AllRecipeUsers lists so
		///     building the research index never falls back to the vanilla full-database scan.
		/// </summary>
		private static bool IsSurgery(RecipeDef recipe, Dictionary<RecipeDef, List<ThingDef>> usersByRecipe)
		{
			if (!usersByRecipe.TryGetValue(recipe, out var users))
				return false;

			for (var i = 0; i < users.Count; i++)
			{
				if (users[i] != null && users[i].category == ThingCategory.Pawn)
					return true;
			}

			return false;
		}

		// Read-only wrappers keep callers from mutating the cached lists.
		private static Dictionary<RecipeDef, ReadOnlyCollection<ThingDef>> Wrap(
			Dictionary<RecipeDef, List<ThingDef>> lists)
		{
			var wrapped = new Dictionary<RecipeDef, ReadOnlyCollection<ThingDef>>(lists.Count);
			foreach (var pair in lists)
				wrapped[pair.Key] = pair.Value.AsReadOnly();

			return wrapped;
		}
	}
}