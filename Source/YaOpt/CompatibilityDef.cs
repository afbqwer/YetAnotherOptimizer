using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Verse;
using YaOpt.Defines;

namespace YaOpt
{
	public class CompatibilityDef : Def
	{
		public List<string> BannedOptimizations
		{
			get => bannedOptimizations; set => bannedOptimizations = value;
		}

		public List<string> IgnoredToggleTabCaching
		{
			get => ignoredToggleTabCaching; set => ignoredToggleTabCaching = value;
		}

		[NoTranslate]
		private List<string> bannedOptimizations;

		[NoTranslate]
		private List<string> ignoredToggleTabCaching;

		public void Cache()
		{
			var mod = modContentPack.Name;

			if (BannedOptimizations != null)
			{
				foreach (var bannedOptimization in BannedOptimizations)
				{
					if (YaOptGlobal.Settings.AllOptimizations.All(
							opt => opt.SettingId != bannedOptimization))
					{
						YaOptMod.Error($"CompatibilityDef {defName} from {mod} " +
									   $"couldn't find optimization {bannedOptimization}");
						continue;
					}
					YaOptMod.Debug($"Optimization {bannedOptimization} is banned by {defName} from {mod}.");
					CompatibilityDefines.CachedBannedOptimizations.Add(bannedOptimization);
					CompatibilityDefines.CachedBannedBy[bannedOptimization] =
						"YaOpt.Setting.Note.FromMod".Translate(defName, mod);
				}
			}

			if (IgnoredToggleTabCaching != null)
			{
				foreach (var toggleTabTypeName in IgnoredToggleTabCaching)
				{
					var type = AccessTools.TypeByName(toggleTabTypeName);
					if (type == null)
					{
						YaOptMod.Error($"CompatibilityDef {defName} from {mod} " +
									   $"tried to ignore an inexistent toggle tab {toggleTabTypeName}.");
						continue;
					}
					YaOptMod.Debug($"Toggle tab {toggleTabTypeName} will not be cached because of {defName} from {mod}.");
					CompatibilityDefines.CachedIgnoredToggleTabCaching.Add(type);
				}
			}
		}
	}
}
