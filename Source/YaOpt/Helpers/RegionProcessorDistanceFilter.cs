using System.Runtime.CompilerServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Decides whether a Thing can be discarded before the reachability check in
	/// <c>RegionProcessorClosestThingReachable.ProcessThing</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The checker is split by search shape. Unprioritized searches use both the absolute distance
	/// bound and the best-candidate comparison. Prioritized searches use only the absolute bound:
	/// vanilla's acceptance test always includes <c>num2 &lt; maxDistSquared</c>, so a Thing at or
	/// beyond the maximum distance cannot be selected regardless of its priority. The best-candidate
	/// comparison is not safe there because a farther Thing with a higher priority can still win.
	/// </para>
	/// <para>
	/// A Thing rejected here could not have been selected anyway, and <c>validator</c> was not called
	/// for it in vanilla either. For rejected Things only the
	/// <c>ReachabilityWithinRegion.ThingFromRegionListerReachable</c> call and, on the prioritized
	/// path, the priority getter invocation are skipped; every Thing that can still win keeps the
	/// vanilla call chain, including any foreign patches on that method.
	/// </para>
	/// <para>
	/// Both option states are cached by <see cref="Configure"/> during the patch pass, so the hot path
	/// only reads two booleans.
	/// </para>
	/// </remarks>
	/// <seealso cref="Patches.Verse_RegionProcessorClosestThingReachable_ProcessThing"/>
	internal static class RegionProcessorDistanceFilter
	{
		private static bool _filterUnprioritized;

		private static bool _filterPrioritized;

		internal static void Configure(bool filterUnprioritized, bool filterPrioritized)
		{
			_filterUnprioritized = filterUnprioritized;
			_filterPrioritized = filterPrioritized;
		}

		/// <summary>
		/// Returns true when the original <c>ProcessThing</c> body can be skipped for this Thing.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool ShouldSkip(RegionProcessorClosestThingReachable processor, Thing thing)
		{
			if (processor.priorityGetter != null)
			{
				if (!_filterPrioritized)
					return false;

				return (thing.PositionHeld - processor.root).LengthHorizontalSquared
					>= processor.maxDistSquared;
			}

			if (!_filterUnprioritized)
				return false;

			float distanceSquared = (thing.PositionHeld - processor.root).LengthHorizontalSquared;
			if (distanceSquared >= processor.maxDistSquared)
				return true;

			// Mirrors the negation of `(num > bestPrio || num2 < closestDistSquared)` with num = 0f.
			// Using bestPrio rather than closestThing keeps the guard exact even if a caller seeds
			// the processor with a non-default bestPrio or closestThing.
			if (processor.bestPrio >= 0f && distanceSquared >= processor.closestDistSquared)
				return true;

			return false;
		}
	}
}
