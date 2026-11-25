using HarmonyLib;
using RimWorld;
using System;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Throttles <see cref="Room.UpdateRoomStatsAndRole"/> to at most one run per
	/// <see cref="RoomContentCache.StatsUpdateInterval"/> ticks per room.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla only recomputes while <c>statsAndRoleDirty</c> is set, but that flag is raised by every
	/// non-pawn thing spawned or despawned inside the room, so a stocked room with active hauling is
	/// dirty almost continuously. Each recompute runs all 13 room stat workers and all 23 room role
	/// workers, and several of them walk every cell of the room, which makes this a visible spike
	/// rather than a steady cost.
	/// </para>
	/// <para>
	/// While throttled the previous stats and role are returned and the dirty flag is left set, so the
	/// recompute happens on the first read after the window opens. A room that has never been scored
	/// (<c>role == null</c>) is always allowed through - returning a null role would break every
	/// <c>Role == RoomRoleDefOf.*</c> comparison.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRoomStatsThrottle"/>
	[HarmonyPatch(typeof(Room), nameof(Room.Role), MethodType.Getter)]
	internal static class Verse_Room_Role_Throttle
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptRoomStatsThrottle.Enabled == true;
		}

		static bool Prefix(Room __instance, ref RoomRoleDef __result)
		{
			if (__instance == null || !__instance.statsAndRoleDirty || __instance.role == null)
				return true;

			if (!YaOptGlobal.IsInMainThread || RoomContentCache.TryConsumeStatsUpdate(__instance))
				return true;

			__result = __instance.role;
			return false;
		}
	}

	/// <summary>
	/// Same throttle for the stat read path, which shares the dirty flag and the recompute with
	/// <see cref="Room.Role"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptRoomStatsThrottle"/>
	[HarmonyPatch(typeof(Room), nameof(Room.GetStat))]
	internal static class Verse_Room_GetStat_Throttle
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptRoomStatsThrottle.Enabled == true;
		}

		static bool Prefix(Room __instance, RoomStatDef roomStat, ref float __result)
		{
			if (__instance == null || roomStat == null || !__instance.statsAndRoleDirty || __instance.role == null)
				return true;

			if (!YaOptGlobal.IsInMainThread || RoomContentCache.TryConsumeStatsUpdate(__instance))
				return true;

			__result = __instance.stats == null ? roomStat.roomlessScore : __instance.stats[roomStat];
			return false;
		}
	}

	/// <summary>
	/// Keeps the work table placement preview from bypassing the throttle.
	/// </summary>
	/// <remarks>
	/// <c>StatPart_WorkTableRoomRole.WouldApplyToBuildingIfPlaced</c> runs the same dirty check and
	/// recompute once per frame while a work table is being placed. Without this the throttle would
	/// leave the room dirty and the preview would pay a full recompute every frame for the length of
	/// the window - worse than vanilla, which clears the flag on the first read. The flag is restored
	/// afterwards so the recompute still happens once the window opens.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRoomStatsThrottle"/>
	[HarmonyPatch(typeof(Room), nameof(Room.GetRoomRoleIfBuildingPlaced))]
	internal static class Verse_Room_GetRoomRoleIfBuildingPlaced_Throttle
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptRoomStatsThrottle.Enabled == true;
		}

		static void Prefix(Room __instance, out bool __state)
		{
			__state = false;
			if (__instance == null || !__instance.statsAndRoleDirty || __instance.role == null)
				return;

			if (!YaOptGlobal.IsInMainThread || RoomContentCache.TryConsumeStatsUpdate(__instance))
				return;

			__instance.statsAndRoleDirty = false;
			__state = true;
		}

		/// <summary>
		/// Restores the flag even when the preview throws, otherwise the room would never recompute.
		/// </summary>
		static Exception Finalizer(Room __instance, bool __state, Exception __exception)
		{
			if (__state)
				__instance.statsAndRoleDirty = true;
			return __exception;
		}
	}
}
