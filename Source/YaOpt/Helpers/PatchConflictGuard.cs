using System;
using System.Collections.Generic;
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
		/// <summary>
		/// Appends every foreign patch that conflicts with <paramref name="target"/>, formatted for the
		/// settings tooltip. Uses the same rules as <see cref="FindForeignConflict"/>.
		/// </summary>
		internal static void CollectForeignConflicts(MethodBase target, bool includeAll, List<string> results,
			Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "transpiler", target));
				}
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (compatiblePatch != null && compatiblePatch(target, patch))
					continue;
				if (includeAll || (patch.priority <= Priority.Normal && patch.PatchMethod.ReturnType == typeof(bool)))
					results.Add(Describe(patch.owner, "prefix", target));
			}

			if (includeAll)
			{
				foreach (var patch in info.Postfixes)
				{
					if (patch.owner != harmonyId && patch.owner != earlyId &&
						(compatiblePatch == null || !compatiblePatch(target, patch)))
					{
						results.Add(Describe(patch.owner, "postfix", target));
					}
				}
			}
		}

		internal static void CollectForeignReplacementConflicts(MethodBase target, List<string> results,
			Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "transpiler", target));
				}
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (compatiblePatch != null && compatiblePatch(target, patch))
					continue;

				results.Add(Describe(patch.owner, "prefix", target));
			}
		}

		internal static void CollectForeignPostfixConflicts(MethodBase target, List<string> results,
			Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Postfixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (compatiblePatch != null && compatiblePatch(target, patch))
					continue;

				results.Add(Describe(patch.owner, "postfix", target));
			}
		}

		/// <summary>
		/// Appends every foreign transpiler that touches <paramref name="target"/>.
		/// </summary>
		/// <remarks>
		/// Used by body-replacement transpilers. A foreign prefix is harmless there (it runs around the
		/// replacement and can still skip the method exactly as it would skip the original), but a
		/// foreign transpiler edits the instruction list that a replacement would discard.
		/// </remarks>
		internal static void CollectForeignTranspilers(MethodBase target, List<string> results,
			Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "transpiler", target));
				}
			}
		}

		/// <summary>
		/// Appends every foreign prefix at or below normal priority and every transpiler that touches
		/// <paramref name="target"/>. A replacement prefix that returns false skips every prefix ordered
		/// after it, including plain void instrumentation, so those methods cannot safely be replaced.
		/// </summary>
		internal static void CollectForeignPrefixOrTranspilers(MethodBase target, List<string> results,
			Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (compatiblePatch == null || !compatiblePatch(target, patch))
					results.Add(Describe(patch.owner, "transpiler", target));
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (patch.priority <= Priority.Normal &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "prefix", target));
				}
			}
		}

		/// <summary>
		/// Appends the foreign patches that a short-circuiting prefix at
		/// <paramref name="prefixPriority"/> would skip: every foreign transpiler (the body never runs)
		/// and every foreign prefix ordered after it.
		/// </summary>
		internal static void CollectForeignSkippedByPrefix(MethodBase target, int prefixPriority,
			List<string> results, Func<MethodBase, Patch, bool> compatiblePatch = null)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var mod = YaOptMod.Instance;
			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "transpiler", target));
				}
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
					continue;
				if (patch.priority <= prefixPriority &&
					(compatiblePatch == null || !compatiblePatch(target, patch)))
				{
					results.Add(Describe(patch.owner, "prefix", target));
				}
			}
		}

		private static string Describe(string owner, string kind, MethodBase target)
		{
			return owner + " (" + kind + ") → " + target.DeclaringType?.Name + "." + target.Name;
		}

		/// <summary>
		/// Returns true when any foreign patch at all touches the method.
		/// </summary>
		/// <remarks>
		/// Used where a cached result replays a skipped original: a foreign postfix already ran while the
		/// result was produced and would run again on a replay, applying its transformation twice. A
		/// foreign prefix can likewise change arguments or side effects that then only run once.
		/// </remarks>
		internal static bool HasForeignPatch(MethodBase target)
		{
			if (target == null)
				return true;
			if (YaOptMod.Instance == null)
				return true;

			return FindForeignConflict(target, includeAll: true) != null;
		}

		/// <summary>
		/// Returns a description of the conflicting patch, or null when the method is untouched.
		/// </summary>
		/// <param name="includeAll">
		/// When false only transpilers and boolean prefixes at or below normal priority count as
		/// conflicts; when true every foreign patch, postfixes included, does.
		/// </param>
		internal static string FindForeignConflict(MethodBase target, bool includeAll = false)
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
				if (includeAll || (patch.priority <= Priority.Normal && patch.PatchMethod.ReturnType == typeof(bool)))
					return patch.owner + " (prefix)";
			}

			if (includeAll)
			{
				foreach (var patch in info.Postfixes)
				{
					if (patch.owner != harmonyId && patch.owner != earlyId)
						return patch.owner + " (postfix)";
				}
			}

			return null;
		}
	}
}
