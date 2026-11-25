using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Caches generation inputs that only depend on def data: derp weapon checks, per-def allowed stuff
	/// lists and the recipe used to price a def.
	/// </summary>
	/// <remarks>
	/// Vanilla recomputes these on every candidate during item generation. They are pure functions of the
	/// loaded defs, so the caches are rebuilt on save load and whenever the def counts change.
	/// </remarks>
	internal static class GenerationDefCaches
	{
		private struct AllowedStuffsKey : IEquatable<AllowedStuffsKey>
		{
			internal BuildableDef Def;

			internal TechLevel TechLevel;

			internal bool CheckAllowedInStuffGeneration;

			public bool Equals(AllowedStuffsKey other)
			{
				return Def == other.Def
					&& TechLevel == other.TechLevel
					&& CheckAllowedInStuffGeneration == other.CheckAllowedInStuffGeneration;
			}

			public override bool Equals(object obj)
			{
				return obj is AllowedStuffsKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				var hash = Def?.GetHashCode() ?? 0;
				hash = (hash * 397) ^ (int)TechLevel;
				hash = (hash * 397) ^ (CheckAllowedInStuffGeneration ? 1 : 0);
				return hash;
			}
		}

		private static readonly Dictionary<(ThingDef Thing, ThingDef Stuff), bool> DerpWeapons =
			new Dictionary<(ThingDef Thing, ThingDef Stuff), bool>();

		private static readonly Dictionary<AllowedStuffsKey, List<ThingDef>> AllowedStuffs =
			new Dictionary<AllowedStuffsKey, List<ThingDef>>();

		private static readonly Dictionary<BuildableDef, RecipeDef> CalculableRecipes =
			new Dictionary<BuildableDef, RecipeDef>();

		private static int _defCount = -1;

		static GenerationDefCaches()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static void Invalidate()
		{
			DerpWeapons.Clear();
			AllowedStuffs.Clear();
			CalculableRecipes.Clear();
			_defCount = -1;
		}

		private static void EnsureDefCount()
		{
			var count = DefDatabase<ThingDef>.DefCount + DefDatabase<RecipeDef>.DefCount;
			if (count == _defCount)
				return;

			DerpWeapons.Clear();
			AllowedStuffs.Clear();
			CalculableRecipes.Clear();
			_defCount = count;
		}

		internal static bool TryGetDerpWeapon(ThingDef thing, ThingDef stuff, out bool value)
		{
			EnsureDefCount();
			return DerpWeapons.TryGetValue((thing, stuff), out value);
		}

		internal static void StoreDerpWeapon(ThingDef thing, ThingDef stuff, bool value)
		{
			EnsureDefCount();
			DerpWeapons[(thing, stuff)] = value;
		}

		/// <summary>
		/// Builds the same sequence that <see cref="GenStuff.AllowedStuffsFor"/> yields and caches it.
		/// </summary>
		/// <remarks>
		/// The logic mirrors the vanilla iterator exactly; the yielded list is only ever enumerated by
		/// callers, so a shared list is safe.
		/// </remarks>
		internal static List<ThingDef> BuildAllowedStuffs(BuildableDef def, TechLevel techLevel,
			bool checkAllowedInStuffGeneration)
		{
			EnsureDefCount();

			var key = new AllowedStuffsKey
			{
				Def = def,
				TechLevel = techLevel,
				CheckAllowedInStuffGeneration = checkAllowedInStuffGeneration
			};
			if (AllowedStuffs.TryGetValue(key, out var cached))
				return cached;

			var result = new List<ThingDef>();
			if (def.MadeFromStuff)
			{
				ThingDef found = null;
				var yielded = 0;
				var stuffDefs = GenStuff.StuffDefs;
				for (var i = 0; i < stuffDefs.Count; i++)
				{
					var stuff = stuffDefs[i];
					if (!stuff.IsStuff
						|| (techLevel != TechLevel.Undefined && (int)stuff.techLevel > (int)techLevel)
						|| !stuff.stuffProps.CanMake(def))
					{
						continue;
					}

					found = stuff;
					if (!checkAllowedInStuffGeneration || stuff.stuffProps.allowedInStuffGeneration)
					{
						yielded++;
						result.Add(stuff);
					}
				}

				if (yielded == 0 && found != null)
					result.Add(found);
			}

			AllowedStuffs[key] = result;
			return result;
		}

		internal static bool TryGetCalculableRecipe(BuildableDef def, out RecipeDef recipe)
		{
			EnsureDefCount();
			return CalculableRecipes.TryGetValue(def, out recipe);
		}

		internal static void StoreCalculableRecipe(BuildableDef def, RecipeDef recipe)
		{
			EnsureDefCount();
			CalculableRecipes[def] = recipe;
		}
	}
}
