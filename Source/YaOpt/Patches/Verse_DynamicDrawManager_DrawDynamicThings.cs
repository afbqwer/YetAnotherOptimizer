using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Updates wind material parameters on render frames instead of every logic tick.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptWindUpdate"/>
	[ManualPatch]
	internal static class Verse_DynamicDrawManager_DrawDynamicThings
	{
		static void Patch(Harmony harmony)
		{
			if (!YaOptGlobal.Settings.OptWindUpdate.Enabled)
				return;

			harmony.Patch(AccessTools.Method(
					typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings)),
				prefix: new HarmonyMethod(typeof(Verse_DynamicDrawManager_DrawDynamicThings), nameof(Prefix)));
		}

		static void Prefix()
		{
			WindHelper.UpdateWindForMaterials();
		}
	}
}
