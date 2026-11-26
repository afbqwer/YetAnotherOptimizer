using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the per-system game-time tick loop with an in-place span loop.
	/// </summary>
	/// <remarks>
	/// The per-fleck work lives in <c>TimeInterval</c> and is left untouched; this only removes the
	/// read-copy and write-back around it and hoists the map lookup out of the loop. Systems whose
	/// runtime type is not exactly one of the three vanilla systems keep the vanilla method, because a
	/// subclass may override <c>Tick</c>.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFleckTickLoop"/>
	/// <seealso cref="FleckTickHelper"/>
	[HarmonyPatch(typeof(FleckManager), nameof(FleckManager.FleckManagerTick))]
	internal static class Verse_FleckManager_FleckManagerTick
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptFleckTickLoop.Enabled != true)
				return false;

			// We replace the loop that calls Ticks directly, so a foreign patch on the manager method or
			// an underlying system's Tick would be bypassed. That case is detected up front and disables
			// the option by default; here we only require the targets to exist.
			return AccessTools.Method(typeof(FleckManager), nameof(FleckManager.FleckManagerTick)) != null &&
				   AccessTools.Method(typeof(FleckSystemStatic), nameof(FleckSystem.Tick)) != null &&
				   AccessTools.Method(typeof(FleckSystemThrown), nameof(FleckSystem.Tick)) != null &&
				   AccessTools.Method(typeof(FleckSystemSplash), nameof(FleckSystem.Tick)) != null;
		}

		static bool Prefix(FleckManager __instance)
		{
			foreach (var system in __instance.systems.Values)
			{
				if (system.GetType() == typeof(FleckSystemStatic))
					FleckTickHelper.Tick((FleckSystemStatic)system);
				else if (system.GetType() == typeof(FleckSystemThrown))
					FleckTickHelper.Tick((FleckSystemThrown)system);
				else if (system.GetType() == typeof(FleckSystemSplash))
					FleckTickHelper.Tick((FleckSystemSplash)system);
				else
					system.Tick();
			}
			return false;
		}
	}
}