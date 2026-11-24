using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <seealso cref="YaOptSettings.OptFastListerRemove"/>
	[HarmonyPatch(typeof(JobGiver_AIResurrectTarget))]
	[HarmonyPatch("UpdateResurrectTarget")]
	internal static class RimWorld_JobGiver_AIResurrectTarget_UpdateResurrectTarget
	{
		static bool Prepare()
		{
			// Only meaningful while the index is maintained; otherwise this is a wasted scan
			// over every corpse on the map.
			return YaOptGlobal.Settings.OptFastListerRemove.Enabled &&
				   ListerThingsHelper.FastRemoveAvailable;
		}

		static void Postfix(Pawn pawn)
		{
			ListerThingsHelper.RebuildIndex(pawn.Map.listerThings, ThingRequestGroup.Corpse);
		}
	}
}
