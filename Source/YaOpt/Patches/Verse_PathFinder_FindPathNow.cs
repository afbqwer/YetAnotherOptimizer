using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Redirects the unconditional <c>ForceCompleteScheduledJobs()</c> at the start of the synchronous
	/// <see cref="PathFinder.FindPathNow"/> through <see cref="PathFindNowBarrier.MaybeForceComplete"/>, which
	/// keeps the barrier only when the call still needs to gather data.
	/// </summary>
	/// <remarks>
	/// This targets only the six-argument overload (the one that actually performs the work); the
	/// five-argument overload just forwards to it. The call site leaves <c>this</c> on the stack, which is
	/// exactly the argument the replacement static method consumes, so the IL shape is a straight swap.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathFindNowNoBarrier"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.FindPathNow), new[]
	{
		typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms),
		typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer)
	})]
	internal static class Verse_PathFinder_FindPathNow
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathFindNowNoBarrier.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var target = AccessTools.Method(typeof(PathFinder), nameof(PathFinder.ForceCompleteScheduledJobs));
			var replacement = AccessTools.Method(typeof(PathFindNowBarrier), nameof(PathFindNowBarrier.MaybeForceComplete));
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, target))
				{
					var swapped = new CodeInstruction(OpCodes.Call, replacement);
					swapped.labels.AddRange(instruction.labels);
					swapped.blocks.AddRange(instruction.blocks);
					yield return swapped;
					continue;
				}

				yield return instruction;
			}
		}
	}
}