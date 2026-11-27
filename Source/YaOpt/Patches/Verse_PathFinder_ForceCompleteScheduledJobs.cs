using HarmonyLib;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Completes the outstanding path/grid jobs by looping over the handles directly, avoiding the
	/// per-tick <c>NativeArray&lt;JobHandle&gt;(Allocator.Persistent)</c> that vanilla allocates, fills and
	/// frees every tick.
	/// </summary>
	/// <remarks>
	/// <see cref="JobHandle.CompleteAll(NativeArray{JobHandle})"/> in this runtime is just a loop of
	/// <see cref="JobHandle.Complete"/>, so completing each handle in place is exactly equivalent.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathJobCompletion"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.ForceCompleteScheduledJobs))]
	internal static class Verse_PathFinder_ForceCompleteScheduledJobs
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathJobCompletion.Enabled;
		}

		static bool Prefix(PathFinder __instance)
		{
			var pathJobs = __instance.scheduledPathJobs;
			for (var i = 0; i < pathJobs.Count; i++)
				pathJobs[i].handle.Complete();

			var gridJobs = __instance.scheduledGridJobs;
			for (var i = 0; i < gridJobs.Count; i++)
				gridJobs[i].handle.Complete();

			return false;
		}
	}
}