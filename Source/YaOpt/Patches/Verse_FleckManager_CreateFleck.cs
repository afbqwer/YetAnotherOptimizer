using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Appends a new fleck by building it in place inside the system's list.
	/// </summary>
	/// <remarks>
	/// The vanilla method looks up the system and then calls <c>FleckSystem.CreateFleck</c>, which
	/// constructs the struct as a local, runs <c>Setup</c> and appends a full copy. This prefix keeps
	/// the lookup and the altitude adjustment identical and only replaces the construction for the three
	/// vanilla systems; anything else (including a mod subclass) falls back to the vanilla method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFleckCreateInPlace"/>
	/// <seealso cref="FleckCreateHelper"/>
	[HarmonyPatch(typeof(FleckManager), nameof(FleckManager.CreateFleck))]
	internal static class Verse_FleckManager_CreateFleck
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptFleckCreateInPlace.Enabled != true)
				return false;

			// A foreign patch on this method or on a system's CreateFleck must keep vanilla behaviour.
			if (PatchConflictGuard.HasForeignConflict(AccessTools.Method(typeof(FleckManager), nameof(FleckManager.CreateFleck))))
				return false;
			if (PatchConflictGuard.HasForeignConflict(AccessTools.Method(typeof(FleckSystemStatic), nameof(FleckSystem.CreateFleck))))
				return false;
			if (PatchConflictGuard.HasForeignConflict(AccessTools.Method(typeof(FleckSystemThrown), nameof(FleckSystem.CreateFleck))))
				return false;
			if (PatchConflictGuard.HasForeignConflict(AccessTools.Method(typeof(FleckSystemSplash), nameof(FleckSystem.CreateFleck))))
				return false;
			return true;
		}

		static bool Prefix(FleckManager __instance, FleckCreationData fleckData)
		{
			if (!__instance.systems.TryGetValue(fleckData.def.fleckSystemClass, out var system))
			{
				// Let the original raise its own error for an unhandled def.
				return true;
			}

			fleckData.spawnPosition.y = fleckData.def.altitudeLayer.AltitudeFor(fleckData.def.altitudeLayerIncOffset);
			if (system.GetType() == typeof(FleckSystemStatic))
				FleckCreateHelper.Create((FleckSystemStatic)system, fleckData);
			else if (system.GetType() == typeof(FleckSystemThrown))
				FleckCreateHelper.Create((FleckSystemThrown)system, fleckData);
			else if (system.GetType() == typeof(FleckSystemSplash))
				FleckCreateHelper.Create((FleckSystemSplash)system, fleckData);
			else
				system.CreateFleck(fleckData);
			return false;
		}
	}
}