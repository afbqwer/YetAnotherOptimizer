using HarmonyLib;
using YaOpt.Helpers;
using YaOpt.Patches;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Installs the value-only replacement for Exosuit's XML class-rename patch operation.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptExosuitXmlFast" />
	[EarlyPatch]
	[ManualPatch]
	internal static class Exosuit_XmlBackCompat_ApplyWorker
	{
		internal static bool Patch(Harmony harmony)
		{
			if (!DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptExosuitXmlFast))
				return true;

			return ExosuitXmlBackCompatFast.Install(harmony);
		}
	}
}