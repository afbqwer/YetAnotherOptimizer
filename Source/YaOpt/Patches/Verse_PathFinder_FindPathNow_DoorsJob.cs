using System.Collections.Generic;
using System.Reflection.Emit;
using Gilzoide.ManagedJobs;
using HarmonyLib;
using Verse;
using Verse.AI;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Runs the synchronous <see cref="PathFinder.FindPathNow"/> door/pawn overlay through
	/// <see cref="DoorsOverlayJob"/> instead of the <see cref="PathGridDoorsBlockedJob"/> class wrapped in a
	/// <see cref="ManagedJob"/>.
	/// </summary>
	/// <remarks>
	/// The IL shape is <c>call GetDoorBlockedJob; stloc managed; ldloca managed; call ManagedJob.Run</c>. The
	/// three construction instructions are dropped, leaving <c>(this, request, ref state)</c> on the stack for
	/// <see cref="DoorsOverlayScheduler.Run"/>. If the shape does not match, the instructions are returned
	/// unchanged.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathDoorsJobFast"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.FindPathNow), new[]
	{
		typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms),
		typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer)
	})]
	internal static class Verse_PathFinder_FindPathNow_DoorsJob
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathDoorsJobFast.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);
			var getDoor = AccessTools.Method(typeof(PathFinder), nameof(PathFinder.GetDoorBlockedJob));
			var run = AccessTools.Method(typeof(ManagedJob), nameof(ManagedJob.Run));
			var fast = AccessTools.Method(typeof(DoorsOverlayScheduler), nameof(DoorsOverlayScheduler.Run));
			if (getDoor == null || run == null || fast == null)
				return list;

			for (var i = 0; i < list.Count; i++)
			{
				if (list[i].opcode != OpCodes.Call || !Equals(list[i].operand, getDoor))
					continue;
				if (i + 3 >= list.Count ||
					!IsStloc(list[i + 1].opcode) ||
					!IsLdloca(list[i + 2].opcode) ||
					list[i + 3].opcode != OpCodes.Call ||
					!Equals(list[i + 3].operand, run))
				{
					return list;
				}

				var runCall = list[i + 3];
				runCall.labels.InsertRange(0, list[i + 1].labels);
				runCall.labels.InsertRange(0, list[i + 2].labels);
				runCall.labels.InsertRange(0, list[i].labels);
				runCall.operand = fast;

				var result = new List<CodeInstruction>(list.Count - 3);
				for (var k = 0; k < list.Count; k++)
				{
					if (k == i || k == i + 1 || k == i + 2)
						continue;
					result.Add(list[k]);
				}
				return result;
			}

			return list;
		}

		private static bool IsStloc(OpCode opcode)
		{
			return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
				opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
				opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
		}

		private static bool IsLdloca(OpCode opcode)
		{
			return opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S;
		}
	}
}