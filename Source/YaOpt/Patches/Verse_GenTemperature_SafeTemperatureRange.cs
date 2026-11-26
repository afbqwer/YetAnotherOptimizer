using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="GenTemperature"/>'s apparel-aware safe temperature range with one that
	/// reuses the pawn's base comfy stats during an apparel generation session.
	/// </summary>
	/// <remarks>
	/// <c>PossibleApparelSet.SafeTemperature</c> calls this overload on every rejection-sampling
	/// attempt, and each call runs two full <see cref="RimWorld.StatExtension.GetStatValue"/> pipelines
	/// whose result cannot change while the pawn's gear is being rebuilt. The apparel sums and the
	/// 10 degree safety margin are still recomputed exactly as vanilla does, so the returned range and
	/// therefore the acceptance decisions are identical. Disabled when another mod patches this
	/// overload, because a replay would skip its code.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="PawnGearSessionCaches.TryGetComfyTemperature"/>
	[HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.SafeTemperatureRange),
		new[] { typeof(Pawn), typeof(List<ThingStuffPair>) })]
	internal static class Verse_GenTemperature_SafeTemperatureRange
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptPawnGearSessionCache.Enabled != true || PawnGearRebuildCompat.Ready)
				return false;

			var target = AccessTools.Method(typeof(GenTemperature), nameof(GenTemperature.SafeTemperatureRange),
				new[] { typeof(Pawn), typeof(List<ThingStuffPair>) });
			return !PatchConflictGuard.HasForeignPatch(target);
		}

		static bool Prefix(Pawn p, List<ThingStuffPair> apparel, ref FloatRange __result)
		{
			if (!PawnGearSessionCaches.TryGetComfyTemperature(p, out var min, out var max))
				return true;

			if (apparel != null)
			{
				for (var i = 0; i < apparel.Count; i++)
				{
					min -= apparel[i].InsulationCold;
					max += apparel[i].InsulationHeat;
				}
			}

			__result = new FloatRange(min - 10f, max + 10f);
			return false;
		}
	}
}
