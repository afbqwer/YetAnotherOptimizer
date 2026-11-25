using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Publishes pre-tick and post-tick lifecycle callbacks.
	/// </summary>
	[HarmonyPatch(typeof(TickManager))]
	[HarmonyPatch(nameof(TickManager.DoSingleTick))]
	internal static class Verse_TickManager_DoSingleTick
	{
		static void Prefix()
		{
			UpdateCallbackHelper.PreTick();
		}

		static void Postfix()
		{
			UpdateCallbackHelper.PostTick();
		}
	}
}
