using System;
using System.Collections.Generic;
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
				changed |= HardBan(nameof(YaOptSettings.OptGasGridTick), fish);
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
			var reasons = new List<string>();
			foreach (var target in targets)
			{
				if (target == null)
				{
					continue;
				}

				PatchConflictGuard.CollectForeignConflicts(target, includeAll: false, reasons);
			}

			if (reasons.Count == 0)
			{
				return false;
			}

			var changed = DefaultOff(settingId, reasons);
			YaOptMod.Warning($"{settingId} disabled by default: {string.Join(", ", reasons)}");
			return changed;
		}

		private static bool HardBan(string settingId, string reason)
		{
			if (CompatibilityDefines.CachedBannedOptimizations.Contains(settingId))
			{
				return false;
			}

			CompatibilityDefines.Ban(settingId, reason);
			return true;
		}

		/// <summary>
		/// Disables the optimization by default because a foreign patch was detected. The player can
		/// still enable it, so this is not a ban.
		/// </summary>
		private static bool DefaultOff(string settingId, IReadOnlyList<string> reasons)
		{
			return CompatibilityDefines.PatchConflict(settingId, reasons);
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
