using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Moves the cheap squared-distance comparison ahead of the reachability check in
	/// <c>RegionProcessorClosestThingReachable.ProcessThing</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla calls <c>ReachabilityWithinRegion.ThingFromRegionListerReachable</c> for every Thing
	/// in a Region, then computes the priority and distance and short-circuits on the distance
	/// limits. The transpiler prepends a guard that rejects a Thing on arithmetic alone when it is
	/// beyond <c>maxDistSquared</c>, and on unprioritized searches also when it cannot beat the
	/// current <c>closestDistSquared</c>. The remaining body, including the reachability call,
	/// validator and result assignment, is left byte-for-byte intact for every Thing that can still
	/// be selected.
	/// </para>
	/// <para>
	/// The absolute maximum-distance bound is safe on either path because vanilla's acceptance test
	/// always requires the squared distance to be below <c>maxDistSquared</c>. The best-candidate
	/// comparison is only safe when <c>priorityGetter</c> is null; with a priority getter a farther
	/// Thing with a higher priority can still win. The two scopes are controlled by separate options
	/// and cached in <see cref="RegionProcessorDistanceFilter.Configure"/>.
	/// </para>
	/// <para>
	/// The method body is fixed: one <c>ThingFromRegionListerReachable</c> call and no exception
	/// blocks at entry. Any other shape leaves the body untouched.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptRegionProcessorDistFilter"/>
	/// <seealso cref="YaOptSettings.OptRegionProcessorPriorityDistFilter"/>
	/// <seealso cref="RegionProcessorDistanceFilter"/>
	[HarmonyPatch]
	internal static class Verse_RegionProcessorClosestThingReachable_ProcessThing
	{
		/// <summary>
		/// Resolves the private <c>ProcessThing</c> explicitly so a missing member skips this patch
		/// instead of failing the whole patch pass.
		/// </summary>
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(RegionProcessorClosestThingReachable), "ProcessThing");
		}

		static bool Prepare()
		{
			var target = TargetMethod();
			var settings = YaOptGlobal.Settings;
			var filterUnprioritized = settings?.OptRegionProcessorDistFilter.Enabled == true;
			var filterPrioritized = settings?.OptRegionProcessorPriorityDistFilter.Enabled == true;
			RegionProcessorDistanceFilter.Configure(filterUnprioritized, filterPrioritized);
			return target != null && (filterUnprioritized || filterPrioritized);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
			ILGenerator generator)
		{
			var list = new List<CodeInstruction>(instructions);
			var helper = AccessTools.Method(typeof(RegionProcessorDistanceFilter),
				nameof(RegionProcessorDistanceFilter.ShouldSkip));
			var reachability = AccessTools.Method(typeof(ReachabilityWithinRegion),
				nameof(ReachabilityWithinRegion.ThingFromRegionListerReachable));

			if (helper == null || reachability == null || list.Count == 0 || list[0].blocks.Count != 0)
			{
				YaOptMod.Debug("Region processor distance filter: unexpected method shape, patch skipped.");
				return list;
			}

			var callCount = 0;
			for (var i = 0; i < list.Count; i++)
			{
				if (list[i].Calls(reachability))
					callCount++;
			}

			if (callCount != 1)
			{
				YaOptMod.Debug("Region processor distance filter: reachability call count is " + callCount +
							   ", patch skipped.");
				return list;
			}

			var continueLabel = generator.DefineLabel();
			var guard = new[]
			{
				CodeInstruction.LoadArgument(0),
				CodeInstruction.LoadArgument(2),
				CodeInstruction.Call(typeof(RegionProcessorDistanceFilter),
					nameof(RegionProcessorDistanceFilter.ShouldSkip)),
				new CodeInstruction(OpCodes.Brfalse, continueLabel),
				new CodeInstruction(OpCodes.Ret)
			};

			// Keep any incoming labels or exception blocks on the original entry instruction.
			guard[0].labels.AddRange(list[0].labels);
			guard[0].blocks.AddRange(list[0].blocks);
			list[0].labels.Clear();
			list[0].blocks.Clear();
			list[0].labels.Add(continueLabel);

			list.InsertRange(0, guard);
			YaOptMod.Debug("Region processor distance filter: prepended the distance guard.");
			return list;
		}
	}
}
