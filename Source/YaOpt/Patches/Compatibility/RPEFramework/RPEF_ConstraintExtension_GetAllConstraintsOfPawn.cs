using HarmonyLib;
using System;
using System.Reflection;
using YaOpt.Helpers.ThreadSafe;

namespace YaOpt.Patches.Compatibility.RPEFramework
{
	[HarmonyPatch]
	internal static class RPEF_ConstraintExtension_GetAllConstraintsOfPawn
	{
		private static GreedySpinLock _spinLock = new GreedySpinLock();

		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("RPEF.ConstraintExtension");
			// Newer RPEF versions replaced the iterator with a plain method backed by a
			// static per-tick cache, so the shared state lives in that method itself.
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
			{
				if (method.Name == "GetAllConstraintsOfPawn" && method.ReturnType == typeof(void))
					return method;
			}
			// Older RPEF versions expose it as an iterator, whose state machine is what
			// actually touches the shared buffers.
			foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic))
			{
				if (!nested.Name.Contains("GetAllConstraintsOfPawn"))
					continue;
				var moveNext = nested.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
				if (moveNext != null)
					return moveNext;
			}
			throw new MissingMethodException(
				"Cannot find GetAllConstraintsOfPawn for RPEF.ConstraintExtension. " +
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