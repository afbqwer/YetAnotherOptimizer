using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="BeautyUtility.AverageBeautyPerceptible"/> with <see cref="BeautyFastPath"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The vanilla method is the single hottest entry of the beauty system: the need tick, the think
	/// tree and two per-frame UI drawers all call it, and each call samples 241 cells that each walk
	/// the full stat pipeline for every relevant thing. The rewrite keeps the same sampling, the same
	/// visibility rules and the same numbers, and only removes the repeated work.
	/// </para>
	/// <para>
	/// This replaces the whole method body, so any other mod's patch on it is bypassed while the
	/// option is enabled. <see cref="PatchConflictDetector"/> lowers the option's default when such a
	/// patch is seen; the patch itself only honours the option state.
	/// </para>
	/// </remarks>
	/// <seealso cref="BeautyFastPath"/>
	/// <seealso cref="YaOptSettings.OptBeautyFastPath"/>
	[HarmonyPatch(typeof(BeautyUtility))]
	[HarmonyPatch(nameof(BeautyUtility.AverageBeautyPerceptible))]
	internal static class RimWorld_BeautyUtility_AverageBeautyPerceptible
	{
		static bool Prepare()
		{
			BeautyFastPath.Invalidate();
			return YaOptGlobal.Settings?.OptBeautyFastPath.Enabled == true;
		}

		static bool Prefix(IntVec3 root, Map map, ref float __result)
		{
			return !BeautyFastPath.TryCompute(root, map, out __result);
		}
	}
}
