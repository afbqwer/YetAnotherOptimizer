using HarmonyLib;
using Verse.AI;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Defers toil advances that exceed the per-tick budget so a synchronous toil loop cannot
	/// recurse until the process dies.
	/// </summary>
	/// <remarks>
	/// Every synchronous advancement funnels through this method: ReadyForNextToil, JumpToToil,
	/// the first toil of a new job and the DriverTick cascade. Returning false without advancing
	/// leaves wantBeginNextToil set, which makes DriverTick retry on the next tick.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptToilRecursionGuard"/>
	/// <seealso cref="ToilRecursionGuard"/>
	[HarmonyPatch(typeof(JobDriver))]
	[HarmonyPatch(nameof(JobDriver.TryActuallyStartNextToil))]
	internal static class Verse_AI_JobDriver_TryActuallyStartNextToil
	{
		private static bool Prepare()
		{
			return PrepatcherState.ToilGuardActive;
		}

		private static bool Prefix(JobDriver __instance)
		{
			return ToilRecursionGuard.TryBeginAdvance(__instance);
		}
	}
}