using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.Glow;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Disables the gas/glow grid optimizations when another mod patches the members they replace
	/// or bypass.
	/// </summary>
	/// <remarks>
	/// Both optimizations read vanilla internals directly (<c>gasDensity</c>, <c>lights</c>,
	/// <c>glowPool</c>) and skip parts of the vanilla methods. A third-party patch on those methods
	/// would either be silently skipped or feed stale data, so it is safer to fall back to vanilla.
	/// The check runs before patching on every <see cref="Patches.Patcher.TryPatch"/> so the ban is
	/// already visible to each patch's <c>Prepare</c>.
	/// </remarks>
	internal static class GridOptimizationCompat
	{
		/// <summary>
		/// Refreshes the conflict bans before patching.
		/// </summary>
		/// <returns>True when a new optimization was banned.</returns>
		internal static bool Refresh()
		{
			var changed = false;
			var fish = PerformanceFishReason();
			if (fish != null)
			{
				changed |= Ban(nameof(YaOptSettings.OptGasGridTick), fish);
			}

			changed |= Check(nameof(YaOptSettings.OptGasGridTick), GasTargets());
			changed |= Check(nameof(YaOptSettings.OptGlowGridCombine), GlowTargets());
			return changed;
		}

		private static MethodInfo[] GasTargets()
		{
			return new[]
			{
				AccessTools.Method(typeof(GasGrid), nameof(GasGrid.Tick)),
				AccessTools.Method(typeof(GasGrid), nameof(GasGrid.SetDirect),
					new[] { typeof(int), typeof(byte), typeof(byte), typeof(byte), typeof(byte) }),
				AccessTools.Method(typeof(GasGrid), nameof(GasGrid.SetDirect),
					new[] { typeof(int), typeof(uint) }),
				AccessTools.Method(typeof(GasGrid), nameof(GasGrid.RecalculateEverHadGas))
			};
		}

		private static MethodInfo[] GlowTargets()
		{
			return new[]
			{
				AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First)),
				AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.DirtyLightsAround)),
				AccessTools.Method(typeof(ComputeGlowGridsJob), nameof(ComputeGlowGridsJob.Execute)),
				AccessTools.Method(typeof(ComputeGlowGridsJob), nameof(ComputeGlowGridsJob.PrepareFill)),
				AccessTools.Method(typeof(ComputeGlowGridsJob), nameof(ComputeGlowGridsJob.Flood)),
				AccessTools.Method(typeof(ComputeGlowGridsJob), nameof(ComputeGlowGridsJob.SetGlowFromDist)),
				AccessTools.Method(typeof(GlowGrid.CombineColorsJob), nameof(GlowGrid.CombineColorsJob.Execute)),
				AccessTools.Method(typeof(GlowGrid.CombineColorsJob), nameof(GlowGrid.CombineColorsJob.AddColors)),
				AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.DirtyCell)),
				AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.DevDirtyRect))
			};
		}

		private static bool Check(string settingId, MethodInfo[] targets)
		{
			foreach (var target in targets)
			{
				if (target == null)
				{
					continue;
				}

				var conflict = FindForeignConflict(target);
				if (conflict == null)
				{
					continue;
				}

				var changed = Ban(settingId, "其他 Mod 补丁：" + conflict + " → " +
										  target.DeclaringType?.Name + "." + target.Name);
				YaOptMod.Warning($"{settingId} disabled: {conflict} patches " +
								 $"{target.DeclaringType?.FullName}.{target.Name}.");
				return changed;
			}

			return false;
		}

		/// <summary>
		/// Only conflicts that would silently change behaviour count: a transpiler rewrites the body we
		/// replace or bypass, and a boolean prefix at or below our priority can either short-circuit us
		/// or be short-circuited by our replacement.
		/// </summary>
		/// <remarks>
		/// Pure instrumentation coexists: postfixes always run, and a prefix above priority 400 (for
		/// example GPP's probes at 600/800) runs before ours. That keeps profiling mods usable.
		/// </remarks>
		private static string FindForeignConflict(MethodBase target)
		{
			var info = Harmony.GetPatchInfo(target);
			if (info == null)
			{
				return null;
			}

			var mod = YaOptMod.Instance;
			if (mod == null)
			{
				return null;
			}

			var harmonyId = mod.Harmony.Id;
			var earlyId = mod.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (patch.owner != harmonyId && patch.owner != earlyId)
				{
					return patch.owner + " (transpiler)";
				}
			}

			foreach (var patch in info.Prefixes)
			{
				if (patch.owner == harmonyId || patch.owner == earlyId)
				{
					continue;
				}
				if (patch.priority > Priority.Normal)
				{
					continue;
				}
				if (patch.PatchMethod.ReturnType == typeof(bool))
				{
					return patch.owner + " (prefix)";
				}
			}

			return null;
		}

		private static bool Ban(string settingId, string reason)
		{
			if (CompatibilityDefines.CachedBannedOptimizations.Contains(settingId))
			{
				return false;
			}

			CompatibilityDefines.CachedBannedOptimizations.Add(settingId);
			CompatibilityDefines.CachedBannedBy[settingId] = reason;
			return true;
		}

		/// <summary>
		/// Performance Fish rewrites <c>GasGrid.Tick</c> and <c>SetDirect</c> with Prepatcher, which
		/// leaves no trace in Harmony's patch registry, so it has to be handled by mod id.
		/// </summary>
		private static string PerformanceFishReason()
		{
			return YaOptGlobal.HasMod("bs.performance") ? "Performance Fish 的气体网格优化" : null;
		}
	}
}
