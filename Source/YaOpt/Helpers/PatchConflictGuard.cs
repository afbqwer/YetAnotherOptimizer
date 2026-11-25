using System;
using System.Reflection;
using HarmonyLib;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Decides whether a vanilla method can be safely short-circuited by one of our caches.
	/// </summary>
	/// <remarks>
	/// A prefix that returns false skips every prefix ordered after it, so caching over a method that
	/// another mod patches can silently disable that mod's logic. Only transpilers and boolean prefixes
	/// at or below normal priority count as conflicts; instrumentation postfixes coexist.
	/// </remarks>
	internal static class PatchConflictGuard
	{
		internal static bool HasForeignConflict(MethodBase target)
		{
			if (target == null)
				return true;
			if (YaOptMod.Instance == null)
				return true;

			return FindForeignConflict(target) != null;
		}

		/// <summary>
		/// Returns a description of the conflicting patch, or null when the method is untouched.
		/// </summary>
		internal static string FindForeignConflict(MethodBase target)
		{
			if (target == null || YaOptMod.Instance == null)
				return null;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return null;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId)
					return patch.owner + " (transpiler)";
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (patch.priority > Priority.Normal)
					continue;
				if (patch.PatchMethod.ReturnType == typeof(bool))
					return patch.owner + " (prefix)";
			}

			return null;
		}
	}
}
