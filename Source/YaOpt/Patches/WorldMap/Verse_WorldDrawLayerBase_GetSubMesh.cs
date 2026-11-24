using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the per-tile linear submesh scan with a per-layer material index.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptWorldMapSubMeshCache"/>
	[ManualPatch]
	internal static class Verse_WorldDrawLayerBase_GetSubMesh
	{
		internal static bool Patch(Harmony harmony)
		{
			if (!WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapSubMeshCache))
				return true;

			var target = AccessTools.Method(typeof(WorldDrawLayerBase), "GetSubMesh",
				new[] { typeof(Material), typeof(int).MakeByRefType() });
			var clear = AccessTools.Method(typeof(WorldDrawLayerBase), "ClearSubMeshes",
				new[] { typeof(MeshParts) });
			var dispose = AccessTools.Method(typeof(WorldDrawLayerBase), "Dispose");
			if (target == null || clear == null || dispose == null)
				return true;

			harmony.Patch(target,
				prefix: new HarmonyMethod(typeof(Verse_WorldDrawLayerBase_GetSubMesh), nameof(Prefix)),
				postfix: new HarmonyMethod(typeof(Verse_WorldDrawLayerBase_GetSubMesh), nameof(Postfix)));
			harmony.Patch(clear,
				postfix: new HarmonyMethod(typeof(Verse_WorldDrawLayerBase_GetSubMesh), nameof(ClearPostfix)));
			harmony.Patch(dispose,
				postfix: new HarmonyMethod(typeof(Verse_WorldDrawLayerBase_GetSubMesh), nameof(DisposePostfix)));
			return true;
		}

		private static bool Prefix(WorldDrawLayerBase __instance, Material material, ref int subMeshIndex,
			ref LayerSubMesh __result)
		{
			if (material == null)
				return true;

			var list = __instance.subMeshes;
			if (ReferenceEquals(WorldMapSubMeshCache.HintLayer, __instance) &&
				ReferenceEquals(WorldMapSubMeshCache.HintList, list) &&
				ReferenceEquals(WorldMapSubMeshCache.HintMaterial, material) &&
				WorldMapSubMeshCache.HintIndex >= 0 &&
				WorldMapSubMeshCache.HintIndex < list.Count &&
				ReferenceEquals(list[WorldMapSubMeshCache.HintIndex], WorldMapSubMeshCache.HintSubMesh) &&
				WorldMapSubMeshCache.HintSubMesh != null &&
				ReferenceEquals(WorldMapSubMeshCache.HintSubMesh.material, material) &&
				WorldMapSubMeshCache.HintSubMesh.verts.Count < WorldDrawLayerBase.MaxVerticesPerMesh)
			{
				subMeshIndex = WorldMapSubMeshCache.HintIndex;
				__result = WorldMapSubMeshCache.HintSubMesh;
				return false;
			}

			var map = WorldMapSubMeshCache.GetMap(__instance);
			if (!map.TryGetValue(material, out var index) || index < 0 || index >= list.Count)
				return true;

			var candidate = list[index];
			if (candidate == null || !ReferenceEquals(candidate.material, material) ||
				candidate.verts.Count >= WorldDrawLayerBase.MaxVerticesPerMesh)
			{
				map.Remove(material);
				return true;
			}

			WorldMapSubMeshCache.Remember(__instance, list, material, index, candidate);
			subMeshIndex = index;
			__result = candidate;
			return false;
		}

		private static void Postfix(WorldDrawLayerBase __instance, Material material, int subMeshIndex,
			bool __runOriginal)
		{
			if (!__runOriginal || material == null)
				return;

			WorldMapSubMeshCache.GetMap(__instance)[material] = subMeshIndex;
			var list = __instance.subMeshes;
			if (subMeshIndex >= 0 && subMeshIndex < list.Count)
			{
				WorldMapSubMeshCache.Remember(__instance, list, material, subMeshIndex, list[subMeshIndex]);
			}
		}

		private static void ClearPostfix(WorldDrawLayerBase __instance)
		{
			if (WorldMapSubMeshCache.Maps.TryGetValue(__instance, out var map))
				map.Clear();

			if (ReferenceEquals(WorldMapSubMeshCache.HintLayer, __instance))
				WorldMapSubMeshCache.ForgetHint();
		}

		private static void DisposePostfix(WorldDrawLayerBase __instance)
		{
			WorldMapSubMeshCache.Maps.Remove(__instance);
			if (ReferenceEquals(WorldMapSubMeshCache.HintLayer, __instance))
				WorldMapSubMeshCache.ForgetHint();
		}
	}

	/// <summary>
	/// Stores indexes only while the corresponding live submesh still has room. A full submesh is
	/// evicted so the original scan can select the next one, preserving vanilla split ordering.
	/// </summary>
	internal static class WorldMapSubMeshCache
	{
		internal static readonly ConditionalWeakTable<WorldDrawLayerBase, Dictionary<Material, int>> Maps =
			new ConditionalWeakTable<WorldDrawLayerBase, Dictionary<Material, int>>();

		[ThreadStatic]
		internal static WorldDrawLayerBase HintLayer;

		[ThreadStatic]
		internal static List<LayerSubMesh> HintList;

		[ThreadStatic]
		internal static Material HintMaterial;

		[ThreadStatic]
		internal static LayerSubMesh HintSubMesh;

		[ThreadStatic]
		internal static int HintIndex;

		internal static Dictionary<Material, int> GetMap(WorldDrawLayerBase layer)
		{
			return Maps.GetValue(layer, _ => new Dictionary<Material, int>(MaterialReferenceComparer.Instance));
		}

		internal static void Remember(WorldDrawLayerBase layer, List<LayerSubMesh> list, Material material,
			int index, LayerSubMesh subMesh)
		{
			HintLayer = layer;
			HintList = list;
			HintMaterial = material;
			HintSubMesh = subMesh;
			HintIndex = index;
		}

		internal static void ForgetHint()
		{
			HintLayer = null;
			HintList = null;
			HintMaterial = null;
			HintSubMesh = null;
			HintIndex = -1;
		}

		private sealed class MaterialReferenceComparer : IEqualityComparer<Material>
		{
			internal static readonly MaterialReferenceComparer Instance = new MaterialReferenceComparer();

			public bool Equals(Material x, Material y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(Material obj)
			{
				return RuntimeHelpers.GetHashCode(obj);
			}
		}
	}
}
