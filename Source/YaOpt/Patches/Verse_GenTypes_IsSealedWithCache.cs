using HarmonyLib;
using System;
using System.Collections.Concurrent;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the global lock in <see cref="GenTypes.IsSealedWithCache"/> with a lock-free lookup.
	/// </summary>
	/// <remarks>
	/// 1.7 still locks a process-wide dictionary on every cache hit. This is on the
	/// <c>ThingWithComps.GetComp&lt;T&gt;</c> path for missing components.
	/// <c>Type.IsSealed</c> is immutable, so the cache never needs invalidation.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptGenTypesLockFree"/>
	[HarmonyPatch(typeof(GenTypes), nameof(GenTypes.IsSealedWithCache))]
	internal static class Verse_GenTypes_IsSealedWithCache
	{
		private static readonly ConcurrentDictionary<Type, bool> Cache =
			new ConcurrentDictionary<Type, bool>();

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGenTypesLockFree.Enabled;
		}

		static bool Prefix(Type type, ref bool __result)
		{
			if (type == null)
				return true;

			if (Cache.TryGetValue(type, out var isSealed))
			{
				__result = isSealed;
				return false;
			}

			__result = Cache.GetOrAdd(type, static t => t.IsSealed);
			return false;
		}
	}
}