using System;
using Verse;
using YaOpt.Defines;

namespace YaOpt.Settings
{
	public class OptimizationOption
	{
		internal bool _enabled = true;

		private bool _default = true;

		public bool IsLockedOff => (Flags & OptimizationFlags.LockedOff) != 0;

		public bool Enabled
		{
			get
			{
				if (IsLockedOff)
					return false;
				if (!MultiplayerCompatibility && YaOptGlobal.IsMultiplayer)
					return false;
				if (!string.IsNullOrWhiteSpace(RequiredMod) && !YaOptGlobal.HasMod(RequiredMod))
					return false;
				if (CompatibilityDefines.CachedBannedOptimizations.Contains(SettingId))
					return false;
				return _enabled;
			}
			set
			{
				if (IsLockedOff)
					return;
				_enabled = value;
			}
		}

		public bool Default
		{
			get => _default;
			set
			{
				_default = value;
				_enabled = value && !IsLockedOff;
			}
		}

		public string Name { get; set; } = string.Empty;

		public string Desc { get; set; } = string.Empty;

		public string NoteStability { get; set; } = string.Empty;

		public string NoteCompatibility { get; set; } = string.Empty;

		public string NoteMultithread { get; set; } = string.Empty;

		public string RequiredMod { get; set; } = string.Empty;

		public string SubCategory { get; set; } = string.Empty;

		public string SettingId { get; set; } = string.Empty;

		public OptimizationCategory Category { get; set; }

		public OptimizationFlags Flags { get; set; }

		public Func<YaOptSettings, bool> FuncShow { get; set; } = null;

		public Action<SettingsPanel, Listing_Standard, OptimizationOption> FuncPostDraw { get; set; } = null;

		public Action<YaOptSettings> FuncExposeData { get; set; } = null;

		public bool MultiplayerCompatibility => (Flags & OptimizationFlags.MultiplayerIncompatible) == 0;

		public void Validate()
		{
			if (IsLockedOff)
				_enabled = false;
		}

		public bool ShouldShow(YaOptSettings settings)
		{
			if (IsLockedOff)
				return false;
			if (!MultiplayerCompatibility && YaOptGlobal.IsMultiplayer)
				return false;
			if (!string.IsNullOrWhiteSpace(RequiredMod) && !YaOptGlobal.HasMod(RequiredMod))
				return false;
			if (FuncShow != null && !FuncShow(settings))
				return false;
			return true;
		}
	}
}