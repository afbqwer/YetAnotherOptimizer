using HarmonyLib;
using System.Collections.Generic;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Returns the cached <see cref="Room.ContainedAndAdjacentThings"/> list while the room's regions
	/// and their thing lists are unchanged.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla rebuilds the list on every access - clear, walk each region, re-add every thing through
	/// a <see cref="HashSet{T}"/> - and a single <c>UpdateRoomStatsAndRole</c> asks for it a dozen
	/// times through the stat and role workers.
	/// </para>
	/// <para>
	/// The cached contents are copied back into vanilla's own <c>uniqueContainedThings</c> field
	/// before returning, because <see cref="Room.Vacuum"/>'s setter iterates that field directly
	/// instead of going through the property. Callers still receive the same list object and the same
	/// ordering vanilla would produce.
	/// </para>
	/// <para>
	/// Only the main thread uses the cache. Worker threads fall through to vanilla, which keeps this
	/// compatible with the thread-safe replacement of the same getter.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRoomThingsCache"/>
	[HarmonyPatch(typeof(Room), nameof(Room.ContainedAndAdjacentThings), MethodType.Getter)]
	internal static class Verse_Room_ContainedAndAdjacentThings_Cache
	{
		static bool Prepare()
		{
			return RoomContentCache.IsSupported
				&& YaOptGlobal.Settings?.OptRoomThingsCache.Enabled == true;
		}

		static bool Prefix(Room __instance, ref List<Thing> __result)
		{
			if (__instance == null || !YaOptGlobal.IsInMainThread)
				return true;

			if (!RoomContentCache.TryGetThings(__instance, out var snapshot))
				return true;

			var list = __instance.uniqueContainedThings;
			list.Clear();
			list.AddRange(snapshot);
			__result = list;
			return false;
		}

		static void Postfix(Room __instance, List<Thing> __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null || __result == null || !YaOptGlobal.IsInMainThread)
				return;

			RoomContentCache.StoreThings(__instance, __result);
		}
	}
}
