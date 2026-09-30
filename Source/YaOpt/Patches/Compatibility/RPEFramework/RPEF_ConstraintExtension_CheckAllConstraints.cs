using HarmonyLib;
using System;
using System.Reflection;
using YaOpt.Helpers.ThreadSafe;

namespace YaOpt.Patches.Compatibility.RPEFramework
{
	[HarmonyPatch]
	internal static class RPEF_ConstraintExtension_CheckAllConstraints
	{
		private static GreedySpinLock _spinLock = new GreedySpinLock();

		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("RPEF.ConstraintExtension");
			// Only the (Def, ThingDef, out Constraint) overload touches the shared
			// _defPairFailConstraintCache. The (Def, Pawn, out Constraint) overload merely
			// delegates to it, so the two must not share a non-reentrant lock.
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
			{
				if (method.Name != "CheckAllConstraints")
					continue;
				var parameters = method.GetParameters();
				if (parameters.Length == 3 && parameters[1].ParameterType.FullName == "Verse.ThingDef")
					return method;
			}
			throw new MissingMethodException(
				"Cannot find CheckAllConstraints(Def, ThingDef, out Constraint) for RPEF.ConstraintExtension. " +
				"This may be due to the mod update. Please report this to the YaOpt developers.");
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptParallelWorkGiver.Enabled &&
				   YaOptGlobal.HasType("RPEF.ConstraintExtension");
		}

		static void Prefix(out bool __state)
		{
			__state = false;
			_spinLock.Enter(ref __state);
		}

		static void Finalizer(bool __state)
		{
			if (__state)
				_spinLock.Exit();
		}
	}
}