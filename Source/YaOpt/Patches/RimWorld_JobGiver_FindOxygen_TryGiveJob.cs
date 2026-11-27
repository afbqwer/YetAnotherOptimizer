using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace YaOpt.Patches
{
	[HarmonyPatch(typeof(JobGiver_FindOxygen), "TryGiveJob")]
	internal static class RimWorld_JobGiver_FindOxygen_TryGiveJob
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptOxygenJobFastExit.Enabled == true;
		}

		static bool Prefix(Pawn pawn, ref Job __result)
		{
			if (!ModsConfig.OdysseyActive || !pawn.Map.Biome.inVacuum)
			{
				__result = null;
				return false;
			}

			return true;
		}
	}
}
