using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace YaOpt.Patches.Compatibility.HumanoidAlienRaces
{
	/// <summary>
	/// Reuses one static <see cref="HashSet{T}"/> for Humanoid Alien Races' need-condition postfix.
	/// </summary>
	/// <remarks>
	/// HAR attaches <c>NeedLevelPostfix</c> to <see cref="Need.CurLevel"/> and rebuilds
	/// <c>new HashSet&lt;Type&gt; { typeof(ConditionMood), typeof(ConditionNeed) }</c> on every write,
	/// which is one of the largest per-tick allocations when HAR is loaded.
	/// <c>RegenerateAddonGraphicsWithCondition</c> only reads that argument (it copies it into the
	/// per-pawn <c>regenerateTypes</c> set), so a single shared set can be passed instead.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptHARNeedCondition"/>
	[HarmonyPatch]
	[HarmonyPriority(Priority.First)]
	internal static class AlienRace_HarmonyPatches_NeedLevelPostfix
	{
		private delegate void RegenerateAddonGraphicsDelegate(Pawn pawn, HashSet<Type> types);

		private static readonly HashSet<Type> _conditionTypes = new HashSet<Type>();

		private static RegenerateAddonGraphicsDelegate _regenerate;

		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("AlienRace.HarmonyPatches");
			return type == null ? null : AccessTools.Method(type, "NeedLevelPostfix");
		}

		static bool Prepare()
		{
			if (!YaOptGlobal.Settings.OptHARNeedCondition.Enabled)
				return false;

			var conditionMood = AccessTools.TypeByName("AlienRace.ExtendedGraphics.ConditionMood");
			var conditionNeed = AccessTools.TypeByName("AlienRace.ExtendedGraphics.ConditionNeed");
			var alienComp = AccessTools.TypeByName("AlienRace.AlienPartGenerator+AlienComp");
			if (conditionMood == null || conditionNeed == null || alienComp == null)
				return false;

			var regenerate = AccessTools.Method(
				alienComp,
				"RegenerateAddonGraphicsWithCondition",
				new[] { typeof(Pawn), typeof(HashSet<Type>) });
			if (regenerate == null)
				return false;

			_conditionTypes.Clear();
			_conditionTypes.Add(conditionMood);
			_conditionTypes.Add(conditionNeed);
			_regenerate = AccessTools.MethodDelegate<RegenerateAddonGraphicsDelegate>(regenerate);
			return _regenerate != null;
		}

		static bool Prefix(Pawn __0)
		{
			_regenerate(__0, _conditionTypes);
			return false;
		}
	}
}
