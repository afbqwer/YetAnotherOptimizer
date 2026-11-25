using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.RegisterGlower))]
	internal static class Verse_GlowGrid_RegisterGlower
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}

	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DeRegisterGlower))]
	internal static class Verse_GlowGrid_DeRegisterGlower
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}

	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.RegisterTerrain))]
	internal static class Verse_GlowGrid_RegisterTerrain
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}

	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DeregisterTerrain))]
	internal static class Verse_GlowGrid_DeregisterTerrain
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}

	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.RegisterCustomGlower))]
	internal static class Verse_GlowGrid_RegisterCustomGlower
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}

	/// <summary>
	/// Invalidates the section light index whenever the lights list changes.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptGlowGridCombine"/>
	[HarmonyPatch(typeof(GlowGrid))]
	[HarmonyPatch(nameof(GlowGrid.DeregisterCustomGlower))]
	internal static class Verse_GlowGrid_DeregisterCustomGlower
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGlowGridCombine.Enabled;
		}

		static void Postfix(GlowGrid __instance)
		{
			GlowGridOptimizer.NotifyLightsChanged(__instance);
		}
	}
}
