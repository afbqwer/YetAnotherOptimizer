using HarmonyLib;
using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches the storable thing def set used by <c>ThingFilter.SetAllowAll</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>AllStorableThingDefs</c> is a LINQ query over the whole thing def database - an iterator
	/// allocation plus one <c>EverStorable</c> call per def, so several thousand delegate calls per
	/// enumeration. Its only caller is <c>SetAllowAll</c>, which runs when a storage filter is reset
	/// or when a dialog builds a fresh filter, and those can happen repeatedly while a storage
	/// settings window is open.
	/// </para>
	/// <para>
	/// The result depends only on def data that is fixed once defs finish loading, so it is cached
	/// per def-list instance. A periodic rescan covers mods that rewrite <c>thingCategories</c> or
	/// <c>minifiedDef</c> after load, which no version counter would report.
	/// </para>
	/// <para>
	/// Nothing is cached before <see cref="ProgramState.Playing"/>: during def loading the database
	/// is still being mutated and partially initialised defs would be captured in the result.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptStorableDefsCache"/>
	[HarmonyPatch(typeof(ThingFilter), "AllStorableThingDefs", MethodType.Getter)]
	internal static class Verse_ThingFilter_AllStorableThingDefs
	{
		private const int RescanIntervalTicks = 2000;

		private static readonly AccessTools.FieldRef<List<ThingDef>, int> ListVersion =
			AccessTools.FieldRefAccess<int>(typeof(List<ThingDef>), "_version");

		private sealed class Entry
		{
			// Verse.WeakReference<T> is the non-generic BCL type with a typed Target and no TryGetTarget.
			public System.WeakReference<List<ThingDef>> DefList;

			public int DefListVersion;

			public int StampTick;

			public ThingDef[] Defs;
		}

		private static Entry _entry;

		static bool Prepare()
		{
			// Patches are reapplied when the option toggles, so this doubles as the invalidation point.
			_entry = null;
			return ListVersion != null
				&& YaOptGlobal.Settings?.OptStorableDefsCache.Enabled == true;
		}

		static bool Prefix(ref IEnumerable<ThingDef> __result)
		{
			if (!CanUseCache())
				return true;
			var entry = _entry;
			if (entry == null)
				return true;
			var list = DefDatabase<ThingDef>.AllDefsListForReading;
			if (list == null || !entry.DefList.TryGetTarget(out var cached) || !ReferenceEquals(cached, list))
				return true;
			if (entry.DefListVersion != ListVersion(list))
				return true;
			if (GenTicks.TicksGame - entry.StampTick >= RescanIntervalTicks)
				return true;
			__result = entry.Defs;
			return false;
		}

		static void Postfix(ref IEnumerable<ThingDef> __result, bool __runOriginal)
		{
			if (!__runOriginal || !CanUseCache())
				return;
			var list = DefDatabase<ThingDef>.AllDefsListForReading;
			if (list == null || __result == null)
				return;

			var collected = new List<ThingDef>();
			foreach (var def in __result)
				collected.Add(def);
			var defs = collected.ToArray();

			_entry = new Entry
			{
				DefList = new System.WeakReference<List<ThingDef>>(list),
				DefListVersion = ListVersion(list),
				StampTick = GenTicks.TicksGame,
				Defs = defs
			};
			// Hand the caller the array we just built instead of the iterator it would have walked.
			__result = defs;
		}

		private static bool CanUseCache()
		{
			return Current.ProgramState == ProgramState.Playing;
		}
	}
}
