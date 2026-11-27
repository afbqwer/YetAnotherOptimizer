using HarmonyLib;
using Verse;
using YaOpt.Helpers;
using YaOpt.Prepatch;

namespace YaOpt.Patches
{
	/// <summary>
	/// Initializes lazy faction colors before pawn preparation runs on worker threads.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	[HarmonyPatch(typeof(DynamicDrawManager))]
	[HarmonyPatch(nameof(DynamicDrawManager.DrawDynamicThings))]
	internal static class Verse_DynamicDrawManager_DrawDynamicThings_ParallelMaterialUpdate
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled &&
				PrepatcherState.PreparedMaterialActive;
		}

		static void Prefix()
		{
			ParallelPreDrawHelper.PrepareMaterialState();
		}
	}
}
