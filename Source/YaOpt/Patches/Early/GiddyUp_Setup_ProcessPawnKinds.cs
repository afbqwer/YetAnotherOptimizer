using HarmonyLib;
using YaOpt.Helpers;
using YaOpt.Patches;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Installs the GiddyUp draw-offset persistence when the mod is present and the option is on.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGiddyUpOffsetCache" />
	[EarlyPatch]
	[ManualPatch]
	internal static class GiddyUp_Setup_ProcessPawnKinds
	{
		internal static bool Patch(Harmony harmony)
		{
			if (!DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptGiddyUpOffsetCache))
				return true;

			return GiddyUpOffsetCache.Install(harmony);
		}
	}
}