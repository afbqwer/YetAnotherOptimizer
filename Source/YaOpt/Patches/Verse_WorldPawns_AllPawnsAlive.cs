using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips the rebuild of <c>WorldPawns.allPawnsAliveResult</c> and
	/// <c>WorldPawns.allPawnsAliveOrDeadResult</c> while their source collections are unchanged.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla clears the result list and copies both backing sets into it on every access. The sets
	/// are <see cref="HashSet{T}"/>, so the copy is a bulk <c>CopyTo</c> - cheap per element, but it
	/// still runs for the full world pawn count every single time.
	/// </para>
	/// <para>
	/// The patch does not allocate its own list. It hands back vanilla's own result field, so callers
	/// still receive the same object identity they would get from vanilla and no extra memory is held.
	/// The cache is skipped when the source sets moved, when the result list itself was mutated by a
	/// caller, or when the owning <see cref="WorldPawns"/> instance was replaced by a game load.
	/// </para>
	/// <para>
	/// <c>AllPawnsAliveOrDead</c> is validated against <c>allPawnsAliveResult</c> as well, because that
	/// list is one of its inputs - if the alive list was rebuilt, the combined list has to follow.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWorldPawnsListCache"/>
	internal static class WorldPawnsListCache
	{
		private static readonly AccessTools.FieldRef<List<Pawn>, int> ListVersion =
			AccessTools.FieldRefAccess<int>(typeof(List<Pawn>), "_version");

		private static readonly AccessTools.FieldRef<HashSet<Pawn>, int> SetVersion =
			CreateSetVersionRef();

		// Verse also declares a WeakReference<T>, but it is the non-generic System.WeakReference with a
		// typed Target property and has no TryGetTarget, so the BCL one is used explicitly.
		private static System.WeakReference<WorldPawns> _owner;

		private static State _state;

		private sealed class State
		{
			public int AliveListVersion = -1;

			public int PawnsAliveVersion = -1;

			public int PawnsMothballedVersion = -1;

			public int OrDeadListVersion = -1;

			public int PawnsDeadVersion = -1;
		}

		private static AccessTools.FieldRef<HashSet<Pawn>, int> CreateSetVersionRef()
		{
			try
			{
				return AccessTools.FieldRefAccess<int>(typeof(HashSet<Pawn>), "_version");
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Returns false when the runtime layout does not expose the version counter we key on, in
		/// which case the patch stays disabled instead of silently returning stale lists.
		/// </summary>
		internal static bool IsSupported => ListVersion != null && SetVersion != null;

		private static bool TryGetState(WorldPawns instance, out State state)
		{
			state = null;
			var owner = _owner;
			if (owner == null)
				return false;
			if (!owner.TryGetTarget(out var cached) || !ReferenceEquals(cached, instance))
				return false;
			state = _state;
			return state != null;
		}

		private static void EnsureOwner(WorldPawns instance)
		{
			var owner = _owner;
			if (owner != null && owner.TryGetTarget(out var cached) && ReferenceEquals(cached, instance))
				return;
			_owner = new System.WeakReference<WorldPawns>(instance);
			_state = new State();
		}

		internal static bool TryGetAlive(WorldPawns instance, out List<Pawn> result)
		{
			result = null;
			if (!TryGetState(instance, out var state))
				return false;
			if (state.AliveListVersion != ListVersion(instance.allPawnsAliveResult))
				return false;
			if (state.PawnsAliveVersion != SetVersion(instance.pawnsAlive))
				return false;
			if (state.PawnsMothballedVersion != SetVersion(instance.pawnsMothballed))
				return false;
			result = instance.allPawnsAliveResult;
			return true;
		}

		internal static void StoreAlive(WorldPawns instance, List<Pawn> result)
		{
			EnsureOwner(instance);
			var state = _state;
			state.AliveListVersion = ListVersion(result);
			state.PawnsAliveVersion = SetVersion(instance.pawnsAlive);
			state.PawnsMothballedVersion = SetVersion(instance.pawnsMothballed);
			// The combined list copied this one, so its next read has to rebuild.
			state.OrDeadListVersion = -1;
		}

		internal static bool TryGetAliveOrDead(WorldPawns instance, out List<Pawn> result)
		{
			result = null;
			if (!TryGetState(instance, out var state))
				return false;
			if (state.OrDeadListVersion != ListVersion(instance.allPawnsAliveOrDeadResult))
				return false;
			if (state.AliveListVersion != ListVersion(instance.allPawnsAliveResult))
				return false;
			if (state.PawnsDeadVersion != SetVersion(instance.pawnsDead))
				return false;
			result = instance.allPawnsAliveOrDeadResult;
			return true;
		}

		internal static void StoreAliveOrDead(WorldPawns instance, List<Pawn> result)
		{
			EnsureOwner(instance);
			var state = _state;
			state.OrDeadListVersion = ListVersion(result);
			state.AliveListVersion = ListVersion(instance.allPawnsAliveResult);
			state.PawnsDeadVersion = SetVersion(instance.pawnsDead);
		}
	}

	[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.AllPawnsAlive), MethodType.Getter)]
	internal static class RimWorld_Planet_WorldPawns_AllPawnsAlive
	{
		static bool Prepare()
		{
			return WorldPawnsListCache.IsSupported
				&& YaOptGlobal.Settings?.OptWorldPawnsListCache.Enabled == true;
		}

		static bool Prefix(WorldPawns __instance, ref List<Pawn> __result)
		{
			return __instance == null || !WorldPawnsListCache.TryGetAlive(__instance, out __result);
		}

		static void Postfix(WorldPawns __instance, List<Pawn> __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null || __result == null)
				return;
			WorldPawnsListCache.StoreAlive(__instance, __result);
		}
	}

	[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.AllPawnsAliveOrDead), MethodType.Getter)]
	internal static class RimWorld_Planet_WorldPawns_AllPawnsAliveOrDead
	{
		static bool Prepare()
		{
			return WorldPawnsListCache.IsSupported
				&& YaOptGlobal.Settings?.OptWorldPawnsListCache.Enabled == true;
		}

		static bool Prefix(WorldPawns __instance, ref List<Pawn> __result)
		{
			return __instance == null || !WorldPawnsListCache.TryGetAliveOrDead(__instance, out __result);
		}

		static void Postfix(WorldPawns __instance, List<Pawn> __result, bool __runOriginal)
		{
			if (!__runOriginal || __instance == null || __result == null)
				return;
			WorldPawnsListCache.StoreAliveOrDead(__instance, __result);
		}
	}
}
