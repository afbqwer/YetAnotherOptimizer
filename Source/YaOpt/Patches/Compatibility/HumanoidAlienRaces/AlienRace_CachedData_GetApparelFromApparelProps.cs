using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Compatibility.HumanoidAlienRaces
{
	/// <summary>
	/// Answers Humanoid Alien Races' apparel-properties lookup from a prebuilt def map instead of its
	/// allocating <c>CachedData.GetApparelFromApparelProps</c>.
	/// </summary>
	/// <remarks>
	/// HAR's method captures its argument in a closure before checking its dictionary, so a cache hit still
	/// allocates. The map in <see cref="HarGearCaches"/> reproduces the same first-match lookup. When the
	/// HAR method is missing or renamed the prefix is not installed and HAR keeps its own behaviour.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearHarFastPath"/>
	[HarmonyPatch]
	internal static class AlienRace_CachedData_GetApparelFromApparelProps
	{
		private static MethodBase _target;

		internal static MethodBase Target => _target ??= Resolve();

		static MethodBase TargetMethod()
		{
			return Target;
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGearHarFastPath.Enabled == true && Target != null;
		}

		private static MethodBase Resolve()
		{
			var type = AccessTools.TypeByName("AlienRace.CachedData");
			return type == null
				? null
				: AccessTools.Method(type, "GetApparelFromApparelProps", new[] { typeof(ApparelProperties) });
		}

		static bool Prefix(ApparelProperties __0, ref ThingDef __result)
		{
			return !HarGearCaches.TryGetApparel(__0, out __result);
		}
	}
}