using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using YaOpt.Defines;
using YaOpt.Patches;
using YaOpt.Settings;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Shared guards and startup conflict checks for world-map loading optimizations.
	/// </summary>
	internal static class WorldMapOptimizationHelper
	{
		internal const string DuplicateProviderModId = "assssssqwww.WorldMapProfiler";

		internal static void CheckConflicts()
		{
			if (YaOptGlobal.HasMod(DuplicateProviderModId))
			{
				BanAll("另一个世界地图优化 Mod");
				return;
			}

			var textureGet = AccessTools.Method(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get),
				new[] { typeof(string), typeof(bool) });
			DefaultOffFromTargets(nameof(YaOptSettings.OptWorldMapTextureCache), textureGet);

			var subMeshGet = AccessTools.Method(typeof(WorldDrawLayerBase), "GetSubMesh",
				new[] { typeof(Material), typeof(int).MakeByRefType() });
			var clearSubMeshes = AccessTools.Method(typeof(WorldDrawLayerBase), "ClearSubMeshes",
				new[] { typeof(MeshParts) });
			if (YaOptGlobal.HasMod("wowgag.guiperformancepatch"))
				HardBan(nameof(YaOptSettings.OptWorldMapSubMeshCache), "WOWGAG's Performance Patch");
			else
				DefaultOffFromTargets(nameof(YaOptSettings.OptWorldMapSubMeshCache), subMeshGet, clearSubMeshes);

			if (YaOptGlobal.HasMod("oskarpotocki.vfe.insectoid2"))
			{
				var insectAdd = FindMethod("VFEInsectoids.WorldDrawLayer_Insects", "TryAddMeshForTile",
					new[] { typeof(PlanetTile) });
				if (!VFEInsectoids_WorldDrawLayer_Insects_TryAddMeshForTile.IsAvailable())
					HardBan(nameof(YaOptSettings.OptWorldMapInsectLayer), "VFE Insectoids 版本不兼容");
				else
					DefaultOffFromTargets(nameof(YaOptSettings.OptWorldMapInsectLayer), insectAdd);
			}

			if (YaOptGlobal.HasMod("regrowth.botr.core"))
			{
				var regenerate = FindMethod("ReGrowthCore.WorldDrawLayer_Beautification", "Regenerate", Type.EmptyTypes);
				var fallback = FindMethod("ReGrowthCore.ReGrowthMod", "get_DefaultWMBPTextureFallback", Type.EmptyTypes);
				if (!ReGrowthCore_WorldDrawLayer_Beautification_Regenerate.IsAvailable())
					HardBan(nameof(YaOptSettings.OptWorldMapRegrowth), "ReGrowth 版本不兼容");
				else
					DefaultOffFromTargets(nameof(YaOptSettings.OptWorldMapRegrowth), regenerate, fallback);
			}

			if (YaOptGlobal.HasMod("m00nl1ght.GeologicalLandforms"))
			{
				var tileInfo = AccessTools.TypeByName("GeologicalLandforms.WorldTileInfo");
				if (tileInfo != null && AccessTools.Method(tileInfo, "WarmCache", Type.EmptyTypes) != null)
				{
					HardBan(nameof(YaOptSettings.OptWorldMapLandformsParallel), "Geological Landforms");
				}
				else
				{
					var regenerate = FindMethod("GeologicalLandforms.WorldLayer_Landforms", "Regenerate", Type.EmptyTypes);
					var get = FindMethod("GeologicalLandforms.WorldTileInfo", "Get", new[] { typeof(int), typeof(bool) });
					if (!GeologicalLandforms_WorldLayer_Landforms_Regenerate.IsAvailable())
						HardBan(nameof(YaOptSettings.OptWorldMapLandformsParallel), "Geological Landforms 版本不兼容");
					else
						DefaultOffFromTargets(nameof(YaOptSettings.OptWorldMapLandformsParallel), regenerate, get);
				}
			}
		}

		/// <summary>
		/// Avoids installing a second copy when another provider is active.
		/// </summary>
		internal static bool CanUse(OptimizationOption option)
		{
			return option.Enabled && !YaOptGlobal.HasMod(DuplicateProviderModId);
		}

		private static void BanAll(string reason)
		{
			HardBan(nameof(YaOptSettings.OptWorldMapTextureCache), reason);
			HardBan(nameof(YaOptSettings.OptWorldMapSubMeshCache), reason);
			HardBan(nameof(YaOptSettings.OptWorldMapInsectLayer), reason);
			HardBan(nameof(YaOptSettings.OptWorldMapRegrowth), reason);
			HardBan(nameof(YaOptSettings.OptWorldMapLandformsParallel), reason);
		}

		/// <summary>
		/// A known mod conflict (duplicate provider, bundled feature, incompatible version) locks the
		/// option off.
		/// </summary>
		private static void HardBan(string settingId, string reason)
		{
			CompatibilityDefines.Ban(settingId, reason);
		}

		/// <summary>
		/// Disables the option by default when any foreign patch touches its targets, listing every
		/// conflict; the player may still enable it.
		/// </summary>
		private static void DefaultOffFromTargets(string settingId, params MethodBase[] targets)
		{
			var reasons = new List<string>();
			for (var i = 0; i < targets.Length; i++)
				PatchConflictGuard.CollectForeignConflicts(targets[i], includeAll: true, reasons);

			if (reasons.Count > 0)
				CompatibilityDefines.PatchConflict(settingId, reasons);
		}

		private static MethodInfo FindMethod(string typeName, string methodName, Type[] parameters)
		{
			var type = AccessTools.TypeByName(typeName);
			return type == null ? null : AccessTools.Method(type, methodName, parameters);
		}
	}
}
