using HarmonyLib;
using RimWorld;

namespace YaOpt.Patches.ThreadSafe.Locked
{
	/// <summary>
	/// Prevents worker threads from running (or waiting on) a wealth recount.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The first recount after loading a save iterates every thing on the map with cold stat
	/// caches, which can take seconds on large modded maps. Wrapping that in a spinlock held by
	/// a job worker starves every other thread and trips the deadlock detector (a false positive,
	/// see GitHub issue #7).
	/// </para>
	/// <para>
	/// Therefore the recount is only ever executed on the main thread, exactly like vanilla;
	/// worker threads skip it and read the cached fields, tolerating a few ticks of staleness.
	/// This also prevents two workers from running <c>ForceRecount</c> concurrently, which would
	/// corrupt its shared <c>tmpThings</c> buffer.
	/// </para>
	/// </remarks>
	[HarmonyPatch(typeof(WealthWatcher))]
	[HarmonyPatch("RecountIfNeeded")]
	internal static class RimWorld_WealthWatcher_RecountIfNeeded
	{
		static bool Prepare()
		{
			return YaOptGlobal.NeedThreadSafe;
		}

		static bool Prefix()
		{
			return YaOptGlobal.IsInMainThread;
		}
	}
}
