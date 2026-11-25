using System;
using System.Xml;
using Verse;

namespace YaOpt.Patches.Xml
{
	/// <summary>
	/// Runs the nested <c>match</c> operation only when the named YaOpt optimization is enabled,
	/// and the nested <c>nomatch</c> operation otherwise. The setting id is the property name on
	/// <see cref="YaOptSettings"/> (its <see cref="Settings.OptimizationOption.SettingId"/>).
	/// </summary>
	/// <remarks>
	/// The nested operation's return value is intentionally ignored. A <c>PatchOperationAdd</c>
	/// guarded by an xpath such as <c>[not(cacheable)]</c> returns false when the node already
	/// exists - that is the intended end state, not a failure. Returning true keeps the patch
	/// error log free of false positives.
	/// </remarks>
	public class PatchOperationSettingGate : PatchOperation
	{
#pragma warning disable CS0649 // Written by RimWorld's XML deserializer via reflection.
		private string setting;

		private PatchOperation match;

		private PatchOperation nomatch;
#pragma warning restore CS0649

		protected override bool ApplyWorker(XmlDocument xml)
		{
			var operation = IsSettingEnabled(setting) ? match : nomatch;
			if (operation == null)
				return true;
			try
			{
				operation.Apply(xml);
			}
			catch (Exception ex)
			{
				Log.Warning($"[YaOpt] XML setting gate '{setting}' failed: {ex}");
			}
			return true;
		}

		private static bool IsSettingEnabled(string settingId)
		{
			if (string.IsNullOrEmpty(settingId))
				return false;
			var settings = YaOptGlobal.Settings;
			if (settings == null)
				return false;
			var options = settings.AllOptimizations;
			for (var i = 0; i < options.Count; i++)
			{
				if (options[i].SettingId == settingId)
					return options[i].Enabled;
			}
			Log.Warning($"[YaOpt] XML setting gate references unknown setting '{settingId}'.");
			return false;
		}

		public override string ToString()
		{
			return $"{base.ToString()}({setting})";
		}
	}
}
