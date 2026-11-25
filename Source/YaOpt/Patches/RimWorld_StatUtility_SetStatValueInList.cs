using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Invalidates cached Frame work values after a stat base is written through the vanilla API.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFrameWorkToBuildCache"/>
	[HarmonyPatch(typeof(StatUtility), nameof(StatUtility.SetStatValueInList))]
	internal static class RimWorld_StatUtility_SetStatValueInList
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptFrameWorkToBuildCache.Enabled == true;
		}

		static void Postfix()
		{
			FrameWorkToBuildCache.Invalidate();
		}
	}
}
