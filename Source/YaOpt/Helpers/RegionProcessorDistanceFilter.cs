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
	/// The check is restricted to the unprioritized path (<c>priorityGetter == null</c>), which is
	/// the path used by <see cref="GenClosest.ClosestThingReachable"/>. On that path
	/// <c>num = priorityGetter?.Invoke(t) ?? 0f</c> is always zero, so the vanilla acceptance test
	/// reduces to <c>num2 &lt; maxDistSquared</c> for the first accepted Thing and
	/// <c>num2 &lt; closestDistSquared &amp;&amp; num2 &lt; maxDistSquared</c> afterwards.
	/// </para>
	/// <para>
	/// Consequently the two early-out conditions below are exactly the negation of the vanilla
	/// acceptance test on that path. A Thing rejected here could not have been selected anyway,
	/// and <c>validator</c> was not called for it in vanilla either. Only the
	/// <c>ReachabilityWithinRegion.ThingFromRegionListerReachable</c> call is skipped for those
	/// Things; every Thing that can still win keeps the vanilla call chain, including any foreign
	/// patches on that method.
	/// </para>
	/// </remarks>
	/// <seealso cref="Patches.Verse_RegionProcessorClosestThingReachable_ProcessThing"/>
	internal static class RegionProcessorDistanceFilter
	{
		/// <summary>
		/// Returns true when the original <c>ProcessThing</c> body can be skipped for this Thing.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool ShouldSkip(RegionProcessorClosestThingReachable processor, Thing thing)
		{
			if (processor.priorityGetter != null)
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
