using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Tracks the last tick on which a pawn's situational thoughts were rebuilt.
	/// </summary>
	/// <remarks>
	/// <see cref="Need_Mood.NeedInterval"/> can reach
	/// <see cref="SituationalThoughtHandler.UpdateAllMoodThoughts"/> twice in one tick: once through
	/// <c>CurInstantLevel</c> and once through <c>SituationalThoughtInterval</c>. The second rebuild
	/// sees the same data as the first one, so it can be skipped until the next tick. Entries are kept
	/// in a weak table so a discarded pawn cannot be retained by the cache.
	/// </remarks>
	/// <seealso cref="Patches.RimWorld_SituationalThoughtHandler_UpdateAllMoodThoughts"/>
	/// <seealso cref="YaOptSettings.OptMoodThoughtDedup"/>
	internal static class ThoughtUpdateCache
	{
		private static readonly ConditionalWeakTable<SituationalThoughtHandler, Entry> Entries =
			new ConditionalWeakTable<SituationalThoughtHandler, Entry>();

		private static int _generation;

		static ThoughtUpdateCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(AdvanceGeneration);
		}

		private sealed class Entry
		{
			public int Generation;
			public int LastTick = -1;
		}

		/// <summary>
		/// Returns false when the handler already rebuilt its thoughts this tick and no new dirty
		/// notification arrived since then.
		/// </summary>
		internal static bool ShouldUpdate(SituationalThoughtHandler handler, bool thoughtsDirty)
		{
			var tickManager = Find.TickManager;
			if (tickManager == null)
				return true;

			var now = tickManager.TicksGame;
			var entry = Entries.GetOrCreateValue(handler);
			if (entry.Generation != _generation)
			{
				entry.Generation = _generation;
				entry.LastTick = -1;
			}

			if (entry.LastTick == now && !thoughtsDirty)
				return false;

			entry.LastTick = now;
			return true;
		}

		private static void AdvanceGeneration()
		{
			_generation++;
		}
	}
}