using HarmonyLib;
using RimWorld.Planet;
using System;
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
			if (HasForeignPatch(textureGet))
				Ban(nameof(YaOptSettings.OptWorldMapTextureCache), "其他纹理查找补丁");

			var subMeshGet = AccessTools.Method(typeof(WorldDrawLayerBase), "GetSubMesh",
				new[] { typeof(Material), typeof(int).MakeByRefType() });
			var clearSubMeshes = AccessTools.Method(typeof(WorldDrawLayerBase), "ClearSubMeshes",
				new[] { typeof(MeshParts) });
			if (YaOptGlobal.HasMod("wowgag.guiperformancepatch"))
				Ban(nameof(YaOptSettings.OptWorldMapSubMeshCache), "WOWGAG's Performance Patch");
			else if (HasForeignPatch(subMeshGet) || HasForeignPatch(clearSubMeshes))
				Ban(nameof(YaOptSettings.OptWorldMapSubMeshCache), "其他子网格补丁");

			if (YaOptGlobal.HasMod("oskarpotocki.vfe.insectoid2"))
			{
				var insectAdd = FindMethod("VFEInsectoids.WorldDrawLayer_Insects", "TryAddMeshForTile",
					new[] { typeof(PlanetTile) });
				if (!VFEInsectoids_WorldDrawLayer_Insects_TryAddMeshForTile.IsAvailable())
					Ban(nameof(YaOptSettings.OptWorldMapInsectLayer), "VFE Insectoids 版本不兼容");
				else if (HasForeignPatch(insectAdd))
					Ban(nameof(YaOptSettings.OptWorldMapInsectLayer), "其他昆虫图层补丁");
			}

			if (YaOptGlobal.HasMod("regrowth.botr.core"))
			{
				var regenerate = FindMethod("ReGrowthCore.WorldDrawLayer_Beautification", "Regenerate", Type.EmptyTypes);
				var fallback = FindMethod("ReGrowthCore.ReGrowthMod", "get_DefaultWMBPTextureFallback", Type.EmptyTypes);
				if (!ReGrowthCore_WorldDrawLayer_Beautification_Regenerate.IsAvailable())
					Ban(nameof(YaOptSettings.OptWorldMapRegrowth), "ReGrowth 版本不兼容");
				else if (HasForeignPatch(regenerate) || HasForeignPatch(fallback))
					Ban(nameof(YaOptSettings.OptWorldMapRegrowth), "其他美化层补丁");
			}

			if (YaOptGlobal.HasMod("m00nl1ght.GeologicalLandforms"))
			{
				var tileInfo = AccessTools.TypeByName("GeologicalLandforms.WorldTileInfo");
				if (tileInfo != null && AccessTools.Method(tileInfo, "WarmCache", Type.EmptyTypes) != null)
				{
					Ban(nameof(YaOptSettings.OptWorldMapLandformsParallel), "Geological Landforms");
				}
				else
				{
					var regenerate = FindMethod("GeologicalLandforms.WorldLayer_Landforms", "Regenerate", Type.EmptyTypes);
					var get = FindMethod("GeologicalLandforms.WorldTileInfo", "Get", new[] { typeof(int), typeof(bool) });
					if (!GeologicalLandforms_WorldLayer_Landforms_Regenerate.IsAvailable())
						Ban(nameof(YaOptSettings.OptWorldMapLandformsParallel), "Geological Landforms 版本不兼容");
					else if (HasForeignPatch(regenerate) || HasForeignPatch(get))
						Ban(nameof(YaOptSettings.OptWorldMapLandformsParallel), "其他地质地形补丁");
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
			Ban(nameof(YaOptSettings.OptWorldMapTextureCache), reason);
			Ban(nameof(YaOptSettings.OptWorldMapSubMeshCache), reason);
			Ban(nameof(YaOptSettings.OptWorldMapInsectLayer), reason);
			Ban(nameof(YaOptSettings.OptWorldMapRegrowth), reason);
			Ban(nameof(YaOptSettings.OptWorldMapLandformsParallel), reason);
		}

		private static void Ban(string settingId, string reason)
		{
			CompatibilityDefines.CachedBannedOptimizations.Add(settingId);
			CompatibilityDefines.CachedBannedBy[settingId] = reason;
		}

		private static bool HasForeignPatch(MethodBase target)
		{
			if (target == null)
				return false;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return false;

			var harmony = YaOptMod.Instance.Harmony;
			var earlyHarmony = YaOptMod.Instance.EarlyHarmony;
			foreach (var owner in info.Owners)
			{
				if (owner != harmony.Id && owner != earlyHarmony.Id)
					return true;
			}

			return false;
		}

		private static MethodInfo FindMethod(string typeName, string methodName, Type[] parameters)
		{
			var type = AccessTools.TypeByName(typeName);
			return type == null ? null : AccessTools.Method(type, methodName, parameters);
		}
	}
}
