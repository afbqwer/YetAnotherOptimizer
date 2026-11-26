using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Applies or reverts the automatic fleck instancing flags when a map's fleck manager is built.
	/// </summary>
	/// <remarks>
	/// Applied regardless of the setting on purpose: when the option is turned off and the patches are
	/// re-applied, the next map construction has to be able to revert the flags this mod had set. The
	/// hook runs once per map instead of per tick. Save loading builds the map on the LongEventHandler
	/// worker thread, and GraphicDatabase refuses to create graphics there, so the work is deferred to
	/// the main thread once that long event finishes.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFleckAutoInstancing"/>
	/// <seealso cref="FleckInstancingHelper"/>
	[HarmonyPatch(typeof(FleckManager), MethodType.Constructor, new[] { typeof(Map) })]
	internal static class Verse_FleckManager_Constructor
	{
		static void Postfix()
		{
			if (UnityData.IsInMainThread)
			{
				FleckInstancingHelper.Sync();
				return;
			}

			LongEventHandler.ExecuteWhenFinished(FleckInstancingHelper.Sync);
		}
	}
}