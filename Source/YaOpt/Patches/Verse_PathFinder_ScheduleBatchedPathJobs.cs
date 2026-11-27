using System.Collections.Generic;
using System.Reflection.Emit;
using Gilzoide.ManagedJobs;
using HarmonyLib;
using Verse;
using YaOpt.Helpers.Pathfinding;

namespace YaOpt.Patches
{
	/// <summary>
	/// Rewrites the per-request door/pawn overlay schedule from
	/// <c>GetDoorBlockedJob(request, ref state).Schedule(jobHandle)</c> to a single by-value
	/// <see cref="DoorsOverlayJob"/> schedule.
	/// </summary>
	/// <remarks>
	/// <see cref="PathGridDoorsBlockedJob"/> is a class, so vanilla has to box it in a
	/// <see cref="ManagedJob"/> (a GC handle) and schedule an extra dispose job per request. The IL shape is
	/// <c>call GetDoorBlockedJob; stloc managed; ldloca managed; ldloc jobHandle; call ManagedJob.Schedule</c>.
	/// The three instructions that build the <see cref="ManagedJob"/> are dropped, leaving
	/// <c>(this, request, ref state)</c> on the stack, and the following <c>ldloc jobHandle</c> completes the
	/// four arguments of <see cref="DoorsOverlayScheduler.Schedule"/>. If the shape does not match, the
	/// instructions are returned unchanged.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathDoorsJobFast"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.ScheduleBatchedPathJobs))]
	internal static class Verse_PathFinder_ScheduleBatchedPathJobs
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathDoorsJobFast.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);
			var getDoor = AccessTools.Method(typeof(PathFinder), nameof(PathFinder.GetDoorBlockedJob));
			var schedule = AccessTools.Method(typeof(ManagedJob), nameof(ManagedJob.Schedule));
			var fast = AccessTools.Method(typeof(DoorsOverlayScheduler), nameof(DoorsOverlayScheduler.Schedule));
			if (getDoor == null || schedule == null || fast == null)
				return list;

			for (var i = 0; i < list.Count; i++)
			{
				if (list[i].opcode != OpCodes.Call || !Equals(list[i].operand, getDoor))
					continue;
				if (i + 4 >= list.Count ||
					!IsStloc(list[i + 1].opcode) ||
					!IsLdloca(list[i + 2].opcode) ||
					!IsLdloc(list[i + 3].opcode) ||
					list[i + 4].opcode != OpCodes.Call ||
					!Equals(list[i + 4].operand, schedule))
				{
					return list;
				}

				var loadHandle = list[i + 3];
				loadHandle.labels.InsertRange(0, list[i + 1].labels);
				loadHandle.labels.InsertRange(0, list[i + 2].labels);
				loadHandle.labels.InsertRange(0, list[i].labels);
				list[i + 4].operand = fast;

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

		private static bool IsLdloc(OpCode opcode)
		{
			return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
				opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
				opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
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