using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches <see cref="RecipeDef.AllRecipeUsers"/> until the thing def list or the recipe's own user
	/// list changes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla returns an iterator that walks every <see cref="ThingDef"/> and calls
	/// <c>recipes.Contains(this)</c> on each one. One enumeration therefore costs a linear scan over the
	/// whole def database plus an iterator allocation - tens of microseconds on a modded list.
	/// </para>
	/// <para>
	/// In 1.7 the only callers are <c>MechWorkUtility.AnyWorkMechCouldDo</c> (bill configuration dialog)
	/// and the debug tables, so this buys nothing on its own. It exists for mods that query it from a
	/// tick path, where the cost is unbounded because the result set is small but the scan is not.
	/// </para>
	/// <para>
	/// The cached value is a <see cref="List{T}"/> handed out as <see cref="IEnumerable{T}"/>, matching
	/// what <c>Enumerable.ToList</c> would give a caller. Callers must not mutate it.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRecipeUsersCache"/>
	[HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AllRecipeUsers), MethodType.Getter)]
	internal static class Verse_RecipeDef_AllRecipeUsers
	{
		/// <summary>
		/// Upper bound on how long a cached list is trusted when neither tracked version moved.
		/// Covers mods that mutate <see cref="ThingDef.recipes"/> directly; that list has no version
		/// counter of its own, so a periodic rescan is the only cheap way to notice.
		/// </summary>
		private const int RescanIntervalTicks = 2000;

		private static readonly AccessTools.FieldRef<List<ThingDef>, int> ListVersion =
			AccessTools.FieldRefAccess<int>(typeof(List<ThingDef>), "_version");

		private sealed class Entry
		{
			public List<ThingDef> Users;

			public int ThingDefListVersion;

			public int RecipeUsersVersion;

			public int StampTick;
		}

		private static Entry[] _entries;

		static bool Prepare()
		{
			// Patches are torn down and reapplied on every game load, so this is also the
			// invalidation point for a freshly loaded save.
			_entries = null;
			return YaOptGlobal.Settings?.OptRecipeUsersCache.Enabled == true;
		}

		private static bool TryGetEntry(RecipeDef instance, out Entry entry)
		{
			entry = null;
			var entries = _entries;
			if (entries == null || instance == null)
				return false;
			var index = instance.index;
			if (index >= entries.Length)
				return false;
			entry = entries[index];
			return entry != null;
		}

		static bool Prefix(RecipeDef __instance, ref IEnumerable<ThingDef> __result)
		{
			if (!TryGetEntry(__instance, out var entry))
				return true;
			if (entry.ThingDefListVersion != ListVersion(DefDatabase<ThingDef>.AllDefsListForReading))
				return true;
			var recipeUsers = __instance.recipeUsers;
			if (entry.RecipeUsersVersion != (recipeUsers == null ? 0 : ListVersion(recipeUsers)))
				return true;
			if (GenTicks.TicksGame - entry.StampTick >= RescanIntervalTicks)
				return true;
			__result = entry.Users;
			return false;
		}

		static void Postfix(RecipeDef __instance, ref IEnumerable<ThingDef> __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null)
				return;
			var thingDefList = DefDatabase<ThingDef>.AllDefsListForReading;
			if (thingDefList == null)
				return;
			var count = DefDatabase<RecipeDef>.DefCount;
			if (count == 0)
				return;
			var index = __instance.index;
			if (index >= count)
				return;

			var users = new List<ThingDef>();
			users.AddRange(__result);

			var entries = _entries;
			if (entries == null || entries.Length != count)
			{
				entries = new Entry[count];
				_entries = entries;
			}

			var recipeUsers = __instance.recipeUsers;
			entries[index] = new Entry
			{
				Users = users,
				ThingDefListVersion = ListVersion(thingDefList),
				RecipeUsersVersion = recipeUsers == null ? 0 : ListVersion(recipeUsers),
				StampTick = GenTicks.TicksGame
			};
			__result = users;
		}
	}
}
