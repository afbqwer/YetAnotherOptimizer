using HarmonyLib;
using UnityEngine;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Throttles weather overlay texture panning to once every few ticks.
	/// </summary>
	/// <remarks>
	/// Each vanilla update calls <see cref="Material.GetTextureScale(int)"/> and
	/// <see cref="Material.SetTextureOffset(int, Vector2)"/>, both of which take a lock in Waylith.
	/// Panning is purely visual, so several ticks can be merged into one update without visible change.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWeatherOverlayThrottle"/>
	[HarmonyPatch(typeof(WeatherOverlayDualPanner), nameof(WeatherOverlayDualPanner.TickOverlay))]
	internal static class Verse_WeatherOverlayDualPanner_TickOverlay
	{
		private const int Interval = 4;

		private static readonly int MainTex = Shader.PropertyToID("_MainTex");

		private static readonly int MainTex2 = Shader.PropertyToID("_MainTex2");

		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptWeatherOverlayThrottle.Enabled;
		}

		static bool Prefix(WeatherOverlayDualPanner __instance)
		{
			if (__instance.worldOverlayMat == null)
				return false;

			var tickManager = Find.TickManager;
			if (tickManager == null)
				return true;

			if (tickManager.TicksGame % Interval != 0)
				return false;

			var mat = __instance.worldOverlayMat;
			var multiplier = tickManager.TickRateMultiplier * Interval;

			__instance.worldPan1 -= __instance.worldPanDir1 *
				(__instance.worldOverlayPanSpeed1 * mat.GetTextureScale(MainTex).x * multiplier);
			mat.SetTextureOffset(MainTex, __instance.worldPan1);

			if (mat.HasProperty(MainTex2))
			{
				__instance.worldPan2 -= __instance.worldPanDir2 *
					(__instance.worldOverlayPanSpeed2 * mat.GetTextureScale(MainTex2).x * multiplier);
				mat.SetTextureOffset(MainTex2, __instance.worldPan2);
			}

			return false;
		}
	}
}