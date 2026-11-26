using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Fills zero quest-generation points from the incident target before the quest is built.
	/// </summary>
	/// <remarks>
	/// Dev tools and some edge callers pass points=0, which scales rewards and threats to zero. The fast
	/// chooser already derives points to build its weights; the same value is reused here when it is recent
	/// and belongs to the same target. Explicit positive float or Slate points are never replaced.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptQuestPointsNormalize"/>
	/// <seealso cref="QuestPointsUtility"/>
	[HarmonyPatch]
	internal static class RimWorld_QuestUtility_GenerateQuestAndMakeAvailable
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
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
					yield return method;
			}
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptQuestPointsNormalize.Enabled == true;
		}

		static void Prefix(object[] __args)
		{
			if (__args == null || __args.Length < 2)
				return;

			var floatIndex = -1;
			var points = 0f;
			IIncidentTarget target = null;
			Slate slate = null;

			for (var i = 1; i < __args.Length; i++)
			{
				var arg = __args[i];
				if (arg == null)
					continue;

				if (floatIndex < 0 && arg is float floatArg)
				{
					floatIndex = i;
					points = floatArg;
				}
				else if (target == null && arg is IIncidentTarget incidentTarget)
				{
					target = incidentTarget;
				}
				else if (slate == null && arg is Slate slateArg)
				{
					slate = slateArg;
				}
			}

			if (target == null && slate != null)
				QuestPointsUtility.TryGetTargetFromSlate(slate, out target);

			if (floatIndex >= 0 && !(points <= QuestPointsUtility.Epsilon))
				return;
			if (slate != null && QuestPointsUtility.TryGetSlatePoints(slate, out var slatePoints)
				&& !(slatePoints <= QuestPointsUtility.Epsilon))
			{
				return;
			}
			if (floatIndex < 0 && slate == null)
				return;

			float normalized;
			if (target != null)
			{
				if (!QuestPointsUtility.TryGetRecentAutoPoints(target, out normalized))
					normalized = QuestPointsUtility.ComputeThreatPointsSafe(target);
			}
			else if (!QuestPointsUtility.TryGetRecentAutoPoints(out normalized))
			{
				return;
			}

			if (!(normalized > QuestPointsUtility.Epsilon))
				return;

			if (floatIndex >= 0)
				__args[floatIndex] = normalized;
			if (slate != null)
				QuestPointsUtility.SetSlatePoints(slate, normalized);
		}
	}
}