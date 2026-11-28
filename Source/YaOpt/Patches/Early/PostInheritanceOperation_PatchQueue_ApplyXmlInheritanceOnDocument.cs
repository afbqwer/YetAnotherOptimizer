using HarmonyLib;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Installs the linear rebuild of PostInheritanceOperation's post-inheritance merge.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastPostInheritanceMerge" />
	[EarlyPatch]
	[ManualPatch]
	internal static class PostInheritanceOperation_PatchQueue_ApplyXmlInheritanceOnDocument
	{
		internal static bool Patch(Harmony harmony)
		{
			if (!YaOptGlobal.Settings.OptFastPostInheritanceMerge.Enabled)
				return true;

			return PostInheritanceFastMerge.Install(harmony);
		}
	}
}
