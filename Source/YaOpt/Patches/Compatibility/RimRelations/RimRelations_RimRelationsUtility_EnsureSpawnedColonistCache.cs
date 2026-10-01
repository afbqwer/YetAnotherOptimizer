using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Patches.Compatibility.RimRelations
{
	/// <summary>
	/// Replaces RimRelations' in-place spawned colonist cache rebuild with a copy-on-write rebuild.
	/// </summary>
	/// <remarks>
	/// RimRelationsUtility.EnsureSpawnedColonistCache clears and refills shared static
	/// collections in place. With the parallel thought update option enabled, thought
	/// workers like ThoughtWorker_ProtectorBond enumerate the cache from job threads
	/// while another thread rebuilds it, throwing "Collection was modified; enumeration
	/// operation may not execute". Snapshots are built privately and published by
	/// reference assignment, so published collections are never mutated and
	/// enumerators stay valid.
	/// </remarks>
	[ManualPatch]
	internal static class RimRelations_RimRelationsUtility_EnsureSpawnedColonistCache
	{
		private static readonly object _rebuildLock = new object();

		private static AccessTools.FieldRef<List<Pawn>> _cacheFieldRef;

		private static AccessTools.FieldRef<Dictionary<int, Pawn>> _byIdFieldRef;

		private static AccessTools.FieldRef<int> _tickFieldRef;

		private delegate bool IsPlayerRelationPawnDelegate(Pawn pawn);

		private static IsPlayerRelationPawnDelegate _isPlayerRelationPawn;

		static bool Patch(Harmony harmony)
		{
			if (!YaOptGlobal.HasMod("reo.rimrelations"))
			{
				return true;
			}

			var type = AccessTools.TypeByName("RimRelations.RimRelationsUtility");
			var target = AccessTools.Method(type, "EnsureSpawnedColonistCache");
			var cacheField = AccessTools.Field(type, "spawnedColonistCache");
			var byIdField = AccessTools.Field(type, "spawnedColonistsById");
			var tickField = AccessTools.Field(type, "spawnedColonistCacheTick");
			var filterMethod = AccessTools.Method(type, "IsPlayerRelationPawn");
			if (target == null || cacheField == null || byIdField == null || tickField == null
				|| filterMethod == null)
			{
				return false;
			}

			_cacheFieldRef = AccessTools.StaticFieldRefAccess<List<Pawn>>(cacheField);
			_byIdFieldRef = AccessTools.StaticFieldRefAccess<Dictionary<int, Pawn>>(byIdField);
			_tickFieldRef = AccessTools.StaticFieldRefAccess<int>(tickField);
			_isPlayerRelationPawn =
				AccessTools.MethodDelegate<IsPlayerRelationPawnDelegate>(filterMethod);

			harmony.Patch(target, prefix: new HarmonyMethod(
				typeof(RimRelations_RimRelationsUtility_EnsureSpawnedColonistCache),
				nameof(EnsureSpawnedColonistCachePrefix)));
			return true;
		}

		// Returns false to fully replace the original rebuild.
		static bool EnsureSpawnedColonistCachePrefix()
		{
			if (!YaOptGlobal.Settings.OptParallelThoughtUpdate.Enabled)
			{
				// Single-threaded, the original rebuild is safe.
				return true;
			}

			int ticksGame = Find.TickManager == null ? -1 : Find.TickManager.TicksGame;
			if (_tickFieldRef() == ticksGame)
			{
				return false;
			}

			lock (_rebuildLock)
			{
				if (_tickFieldRef() == ticksGame)
				{
					return false;
				}

				// Mark the tick first like the original does, so a throw from a malformed
				// pawn does not retry the scan on every call.
				_tickFieldRef() = ticksGame;

				var newCache = new List<Pawn>();
				var newById = new Dictionary<int, Pawn>();
				var maps = Find.Maps;
				if (maps != null)
				{
					for (int i = 0; i < maps.Count; i++)
					{
						var map = maps[i];
						if (map == null || map.mapPawns == null)
						{
							continue;
						}

						var pawns = map.mapPawns.AllPawnsSpawned;
						for (int j = 0; j < pawns.Count; j++)
						{
							var pawn = pawns[j];
							if (!_isPlayerRelationPawn(pawn))
							{
								continue;
							}

							newCache.Add(pawn);
							newById[pawn.thingIDNumber] = pawn;
						}
					}
				}

				// Publish complete snapshots by reference assignment. Enumerators of the
				// previously published collections are never disturbed.
				_cacheFieldRef() = newCache;
				_byIdFieldRef() = newById;
			}

			return false;
		}
	}
}
