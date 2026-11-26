using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Caches the randomly selectable quest roots split by the population-increase flag.
	/// </summary>
	/// <remarks>
	/// The root list only depends on def fields, so it is rebuilt when the quest def count changes or a
	/// save loads. Candidate order follows <see cref="DefDatabase{T}.AllDefsListForReading"/>; selection
	/// builds a weighted random order from it and stops at the first runnable root.
	/// </remarks>
	internal static class QuestRootCache
	{
		private static readonly List<QuestScriptDef> RootsIncPop = new List<QuestScriptDef>(64);

		private static readonly List<QuestScriptDef> RootsNoIncPop = new List<QuestScriptDef>(64);

		private static int _defCount = -1;

		static QuestRootCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static List<QuestScriptDef> GetRoots(bool incPop)
		{
			EnsureBuilt();
			return incPop ? RootsIncPop : RootsNoIncPop;
		}

		private static void EnsureBuilt()
		{
			var allDefs = DefDatabase<QuestScriptDef>.AllDefsListForReading;
			if (allDefs == null || _defCount == allDefs.Count)
				return;

			RootsIncPop.Clear();
			RootsNoIncPop.Clear();
			for (var i = 0; i < allDefs.Count; i++)
			{
				var def = allDefs[i];
				if (def == null || !def.IsRootRandomSelected)
					continue;

				(def.rootIncreasesPopulation ? RootsIncPop : RootsNoIncPop).Add(def);
			}
			_defCount = allDefs.Count;
		}

		private static void Invalidate()
		{
			_defCount = -1;
			RootsIncPop.Clear();
			RootsNoIncPop.Clear();
		}
	}
}