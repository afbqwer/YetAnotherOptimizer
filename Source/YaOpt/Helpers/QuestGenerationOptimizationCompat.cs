using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Protects quest generation optimizations from conflicting patches.
	/// </summary>
	/// <remarks>
	/// The fast selector replaces the chooser result, so any other prefix at or below normal priority
	/// could be skipped or could own the result. Point normalization changes generation arguments, so a
	/// foreign prefix or transpiler on the same overloads can sample or normalize twice. The affected
	/// option is disabled instead of silently overriding the other patch.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptQuestSelectionFastPath"/>
	/// <seealso cref="YaOpt.YaOptSettings.OptQuestAncientComplexFallback"/>
	/// <seealso cref="YaOpt.YaOptSettings.OptQuestPointsNormalize"/>
	internal static class QuestGenerationOptimizationCompat
	{
		private const string FastPathSetting = nameof(YaOpt.YaOptSettings.OptQuestSelectionFastPath);

		private const string FallbackSetting = nameof(YaOpt.YaOptSettings.OptQuestAncientComplexFallback);

		private const string NormalizeSetting = nameof(YaOpt.YaOptSettings.OptQuestPointsNormalize);

		internal static void Refresh()
		{
			CheckChooser();
			CheckPointNormalization();
		}

		private static void CheckChooser()
		{
			var target = AccessTools.Method(typeof(NaturalRandomQuestChooser),
				nameof(NaturalRandomQuestChooser.ChooseNaturalRandomQuest),
				new[] { typeof(float), typeof(IIncidentTarget) });
			if (target == null)
			{
				var reason = "YaOpt.Setting.Note.Conflict.QuestMethodMissing".Translate().ToString();
				DefaultOff(FastPathSetting, reason);
				DefaultOff(FallbackSetting, reason);
				return;
			}

			var reasons = new List<string>();
			PatchConflictGuard.CollectForeignPrefixOrTranspilers(target, reasons);
			if (reasons.Count == 0)
				return;

			DefaultOff(FastPathSetting, reasons);
			DefaultOff(FallbackSetting, reasons);
			YaOptMod.Warning("OptQuestSelectionFastPath disabled by default: " + string.Join(", ", reasons));
		}

		private static void CheckPointNormalization()
		{
			var targets = GetGenerateQuestTargets();
			if (targets.Count == 0)
			{
				DefaultOff(NormalizeSetting,
					"YaOpt.Setting.Note.Conflict.QuestMethodMissing".Translate().ToString());
				return;
			}

			var reasons = new List<string>();
			for (var i = 0; i < targets.Count; i++)
				PatchConflictGuard.CollectForeignPrefixOrTranspilers(targets[i], reasons);

			if (reasons.Count == 0)
				return;

			DefaultOff(NormalizeSetting, reasons);
			YaOptMod.Warning("OptQuestPointsNormalize disabled by default: " + string.Join(", ", reasons));
		}

		private static List<MethodBase> GetGenerateQuestTargets()
		{
			var result = new List<MethodBase>(2);
			var methods = typeof(QuestUtility).GetMethods(
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			for (var i = 0; i < methods.Length; i++)
			{
				var method = methods[i];
				if (method.Name != nameof(QuestUtility.GenerateQuestAndMakeAvailable)
					|| method.ReturnType != typeof(Quest))
				{
					continue;
				}

				var parameters = method.GetParameters();
				if (parameters.Length > 0 && parameters[0].ParameterType == typeof(QuestScriptDef))
					result.Add(method);
			}
			return result;
		}

		private static void DefaultOff(string settingId, string reason)
		{
			CompatibilityDefines.PatchConflict(settingId, reason);
		}

		private static void DefaultOff(string settingId, IReadOnlyList<string> reasons)
		{
			CompatibilityDefines.PatchConflict(settingId, reasons);
		}
	}
}