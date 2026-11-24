using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Bakes ReGrowth's per-tile beautification texture and material decisions by biome.
	/// </summary>
	/// <remarks>
	/// Drawing and random-call order are preserved; missing ReGrowth
	/// members disable the replacement rather than guessing at another version's behavior.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWorldMapRegrowth"/>
	[HarmonyPatch]
	[HarmonyPriority(Priority.First)]
	internal static class ReGrowthCore_WorldDrawLayer_Beautification_Regenerate
	{
		private const int LandmarkRenderQueue = 3505;
		private const int DefaultRenderQueue = 3515;
		private const string NotAnOverlay = "World/MapGraphics/Default";

		private static readonly string[] _snowSuffixByBand = { "", "_SemiSnowy", "_Snowy", "_VerySnowy", "_FullySnowy" };
		private static readonly Color LandmarkColor = new Color(1f, 1f, 1f, 0.5f);

		private static readonly string[] _requiredExtFields =
		{
			"uniqueHills",
			"forested",
			"materialPath",
			"materialLayer",
			"materialSizeMultiplier",
			"smallHillSizeMultiplier",
			"largeHillSizeMultiplier",
			"mountainSizeMultiplier",
			"impassableSizeMultiplier",
			"snowpilesBelow",
			"mountainsSemiSnowyBelow",
			"mountainsSnowyBelow",
			"mountainsVerySnowyBelow",
			"mountainsFullySnowyBelow",
			"impassableSemiSnowyBelow",
			"impassableSnowyBelow",
			"impassableVerySnowyBelow",
			"impassableFullySnowyBelow",
			"forestSnowyBelow",
			"forestSparseBelow",
			"forestDenseAbove",
		};

		private static bool _warned;
		private static MethodInfo _getModExtension;
		private static PropertyInfo _defaultFallback;
		private static Func<WorldDrawLayer, IEnumerable> _baseRegenerate;
		private static Action<object, Tile, Vector3> _drawVanillaHills;

		private static readonly Dictionary<string, FieldInfo> _extFields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
		private static readonly Dictionary<BiomeDef, object> _extByBiome = new Dictionary<BiomeDef, object>();
		private static readonly Dictionary<BiomeDef, Bake> _bakesByBiome = new Dictionary<BiomeDef, Bake>();

		private static readonly Material[] _defaultHills = new Material[6];
		private static readonly Material[] _defaultHillsLandmark = new Material[6];
		private static bool _defaultHillsBuilt;

		static ReGrowthCore_WorldDrawLayer_Beautification_Regenerate()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCaches);
		}

		private static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("ReGrowthCore.WorldDrawLayer_Beautification");
			return type == null ? null : AccessTools.DeclaredMethod(type, "Regenerate", Type.EmptyTypes);
		}

		internal static bool IsAvailable()
		{
			var layerType = AccessTools.TypeByName("ReGrowthCore.WorldDrawLayer_Beautification");
			var extType = AccessTools.TypeByName("ReGrowthCore.BiomesKitControl");
			var modType = AccessTools.TypeByName("ReGrowthCore.ReGrowthMod");
			if (layerType == null || extType == null || modType == null ||
				AccessTools.Method(layerType, "DrawVanillaHills") == null ||
				AccessTools.Property(modType, "DefaultWMBPTextureFallback") == null)
			{
				return false;
			}

			foreach (var name in _requiredExtFields)
			{
				if (AccessTools.Field(extType, name) == null)
					return false;
			}

			return true;
		}

		private static bool Prepare()
		{
			if (!WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapRegrowth))
			return false;

			var layerType = AccessTools.TypeByName("ReGrowthCore.WorldDrawLayer_Beautification");
			var extType = AccessTools.TypeByName("ReGrowthCore.BiomesKitControl");
			var modType = AccessTools.TypeByName("ReGrowthCore.ReGrowthMod");
			if (layerType == null || extType == null || modType == null)
			return false;

			var vanillaHills = AccessTools.Method(layerType, "DrawVanillaHills");
			var baseRegenerateMethod = AccessTools.Method(typeof(WorldDrawLayer), "Regenerate", Type.EmptyTypes);
			var genericExtension = AccessTools.Method(typeof(Def), "GetModExtension", Type.EmptyTypes);
			var fallbackProperty = AccessTools.Property(modType, "DefaultWMBPTextureFallback");
			if (vanillaHills == null || baseRegenerateMethod == null || genericExtension == null ||
				fallbackProperty == null || fallbackProperty.PropertyType != typeof(bool))
			{
				WarnOnce("ReGrowth beautification: required methods are missing; layer keeps its original code.");
				return false;
			}

			_extFields.Clear();
			foreach (var name in _requiredExtFields)
			{
				var field = AccessTools.Field(extType, name);
				if (field == null)
				{
					WarnOnce("ReGrowth beautification: BiomesKitControl." + name +
					" is missing; layer keeps its original code.");
					return false;
				}

				_extFields[name] = field;
			}

			_getModExtension = genericExtension.MakeGenericMethod(extType);
			_defaultFallback = fallbackProperty;
			_baseRegenerate = BuildBaseRegenerate(baseRegenerateMethod);
			_drawVanillaHills = Bind<Action<object, Tile, Vector3>>(vanillaHills, layerType);
			if (_baseRegenerate == null || _drawVanillaHills == null)
			{
				WarnOnce("ReGrowth beautification: delegate binding failed; layer keeps its original code.");
				return false;
			}

			ClearCaches();
			return true;
		}

		internal static void ClearCaches()
		{
			_bakesByBiome.Clear();
			_extByBiome.Clear();
			_defaultHillsBuilt = false;
			Array.Clear(_defaultHills, 0, _defaultHills.Length);
			Array.Clear(_defaultHillsLandmark, 0, _defaultHillsLandmark.Length);
		}

		private static void WarnOnce(string message)
		{
			if (_warned)
			return;

			_warned = true;
			YaOptMod.Warning(message);
		}

		/// <summary>
		/// Calls <c>WorldDrawLayer.Regenerate</c> non-virtually. A plain cast would re-enter this prefix
		/// forever, and Harmony's delegate helper is not guaranteed to emit <c>call</c> for a virtual.
		/// </summary>
		private static Func<WorldDrawLayer, IEnumerable> BuildBaseRegenerate(MethodInfo baseMethod)
		{
			try
			{
				var method = new DynamicMethod(
					"WmpBaseRegenerate",
					typeof(IEnumerable),
					new[] { typeof(WorldDrawLayer) },
					typeof(WorldDrawLayer),
					skipVisibility: true
				);
				ILGenerator il = method.GetILGenerator();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Call, baseMethod);
				il.Emit(OpCodes.Ret);
				return (Func<WorldDrawLayer, IEnumerable>)method.CreateDelegate(typeof(Func<WorldDrawLayer, IEnumerable>));
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Builds an open delegate whose first argument is typed <c>object</c> and cast to the declaring
		/// type inside the stub. The ReGrowth type is not referenced at compile time, and the targets are
		/// private/protected, so neither a cast nor AccessTools.MethodDelegate can be relied on here.
		/// </summary>
		private static T Bind<T>(MethodInfo method, Type instanceType)
		where T : Delegate
		{
			try
			{
				MethodInfo invoke = typeof(T).GetMethod("Invoke")!;
				ParameterInfo[] target = invoke.GetParameters();
				var signature = new Type[target.Length];
				signature[0] = typeof(object);
				for (int i = 1; i < target.Length; i++)
				{
					signature[i] = target[i].ParameterType;
				}

				var stub = new DynamicMethod(
					"WmpBind_" + method.Name,
					invoke.ReturnType,
					signature,
					typeof(ReGrowthCore_WorldDrawLayer_Beautification_Regenerate).Module,
					skipVisibility: true
				);
				ILGenerator il = stub.GetILGenerator();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Castclass, instanceType);
				for (int i = 1; i < target.Length; i++)
				{
					il.Emit(OpCodes.Ldarg, i);
				}

				il.Emit(OpCodes.Call, method);
				il.Emit(OpCodes.Ret);
				return (T)(object)stub.CreateDelegate(typeof(T));
			}
			catch
			{
				return null;
			}
		}

		private static bool Prefix(WorldDrawLayer __instance, ref IEnumerable __result)
		{
			__result = Run(__instance);
			return false;
		}

		private static IEnumerable Run(WorldDrawLayer layer)
		{
			var baseCall = _baseRegenerate;
			var vanillaHills = _drawVanillaHills;
			if (baseCall == null || vanillaHills == null)
			yield break;

			// Keep the base layer's individual long-event steps and chunking points.
			foreach (object item in baseCall(layer))
			yield return item;

			bool fallbackAllowed = ReadFallbackSetting();
			List<Tile> tiles = layer.planetLayer.Tiles;

			Rand.PushState();
			try
			{
				Rand.Seed = Find.World.info.Seed;

				for (int i = 0; i < tiles.Count; i++)
				{
					Tile tile = tiles[i];
					try
					{
						SurfaceTile surface = tile as SurfaceTile;
						Vector3 center = Find.WorldGrid.GetTileCenter(tile.tile);
						BiomeDef biome = tile.PrimaryBiome;

						if (biome != null)
						{
							Bake bake = BakeFor(biome);
							if (bake != null)
							{
								DrawBaked(layer, tile, surface, center, biome, bake, vanillaHills);
								continue;
							}

							// An extension that could not be baked must keep ReGrowth's own semantics.
							if (ExtFor(biome) != null)
							{
								vanillaHills(layer, tile, center);
								continue;
							}
						}

						if (!fallbackAllowed)
						{
							vanillaHills(layer, tile, center);
							continue;
						}

						DrawDefaultHills(layer, tile, center, vanillaHills);
					}
					catch (Exception ex)
					{
						WarnOnce("ReGrowth beautification: tile " + tile.tile + " failed (" +
							ex.GetType().Name + ": " + ex.Message + "); drawing it as vanilla hills.");
						try
						{
							vanillaHills(layer, tile, Find.WorldGrid.GetTileCenter(tile.tile));
						}
						catch
						{
							// Nothing sensible remains for this tile.
						}
					}
				}
			}
			finally
			{
				Rand.PopState();
			}

			layer.FinalizeMesh(MeshParts.All);
		}

		private static void DrawBaked(
			WorldDrawLayer layer,
			Tile tile,
			SurfaceTile surface,
			Vector3 center,
			BiomeDef biome,
			Bake bake,
			Action<object, Tile, Vector3> vanillaHills
		)
		{
			bool roadsClear = !biome.allowRoads || surface?.potentialRoads == null;
			bool riversClear = !biome.allowRivers || surface?.potentialRivers == null;
			bool drewUniqueHills = false;
			int hill = (int)tile.hilliness;

			// Nothing below is hoisted out of the branches: most tiles draw nothing, and reading
			// Landmark / AverageTileSize costs more than the whole per-tile decision.
			if (bake.UniqueHills & roadsClear & riversClear)
			{
				int band = HillsBand(bake, hill, tile.temperature);
				if (band >= 0)
				{
					int index = (hill - 1) * 5 + band;
					Material material = tile.Landmark != null ? bake.HillsLandmark[index] : bake.HillsNormal[index];
					if (material != null)
					{
						LayerSubMesh subMesh = layer.GetSubMesh(material);
						WorldRendererUtility.PrintQuadTangentialToPlanet(
							center,
							center,
							tile.Layer.AverageTileSize * bake.HillSize[hill],
							0.005f,
							subMesh,
							false,
							0.01f,
							false
						);
						WorldRendererUtility.PrintTextureAtlasUVs(Rand.Range(0, 2), Rand.Range(0, 2), 2, 2, subMesh);
						drewUniqueHills = true;
					}
				}
			}

			if ((bake.Forested && hill == 1) & roadsClear & riversClear)
			{
				int forestIndex = (tile.temperature < bake.ForestSnowyBelow ? 3 : 0)
				+ (tile.rainfall < bake.ForestSparseBelow ? 1 : tile.rainfall > bake.ForestDenseAbove ? 2 : 0);
				Material forest = bake.Forest[forestIndex];
				if (forest == null)
				{
					// Original bails out of the whole tile here (`continue`), skipping the overlay branch
					// and the vanilla-hills fallback as well.
					return;
				}

				LayerSubMesh subMesh = layer.GetSubMesh(forest);
				WorldRendererUtility.PrintQuadTangentialToPlanet(
					center,
					center,
					tile.Layer.AverageTileSize * bake.MaterialSizeMultiplier,
					0.005f,
					subMesh,
					false,
					0.01f,
					false
				);
				WorldRendererUtility.PrintTextureAtlasUVs(Rand.Range(0, 2), Rand.Range(0, 2), 2, 2, subMesh);
			}

			if (bake.HasOverlay && bake.Overlay != null)
			{
				LayerSubMesh subMesh = layer.GetSubMesh(bake.Overlay);
				WorldRendererUtility.PrintQuadTangentialToPlanet(
					center,
					center,
					tile.Layer.AverageTileSize * bake.MaterialSizeMultiplier,
					0.005f,
					subMesh,
					false,
					0.01f,
					false
				);
				WorldRendererUtility.PrintTextureAtlasUVs(Rand.Range(0, 2), Rand.Range(0, 2), 2, 2, subMesh);
			}

			if (!drewUniqueHills)
			{
				vanillaHills(layer, tile, center);
			}
		}

		private static void DrawDefaultHills(
			WorldDrawLayer layer,
			Tile tile,
			Vector3 center,
			Action<object, Tile, Vector3> vanillaHills
		)
		{
			BuildDefaultHills();

			int hill = (int)tile.hilliness;
			if (hill < 2 || hill > 5)
			{
				vanillaHills(layer, tile, center);
				return;
			}

			Material material = tile.Landmark != null ? _defaultHillsLandmark[hill] : _defaultHills[hill];
			if (material == null)
			{
				vanillaHills(layer, tile, center);
				return;
			}

			LayerSubMesh subMesh = layer.GetSubMesh(material);
			WorldRendererUtility.PrintQuadTangentialToPlanet(
				center,
				center,
				tile.Layer.AverageTileSize,
				0.005f,
				subMesh,
				false,
				Rand.Range(0f, 360f),
				false
			);
			WorldRendererUtility.PrintTextureAtlasUVs(Rand.Range(0, 2), Rand.Range(0, 2), 2, 2, subMesh);
		}

		private static void BuildDefaultHills()
		{
			if (_defaultHillsBuilt)
			{
				return;
			}

			_defaultHillsBuilt = true;
			for (int hill = 2; hill <= 5; hill++)
			{
				string path = DefaultHillPath(hill);
				if (path == null || ContentFinder<Texture2D>.Get(path, false) == null)
				{
					continue;
				}

				_defaultHills[hill] = MaterialPool.MatFrom(
					path,
					ShaderDatabase.WorldOverlayTransparentLit,
					Color.white,
					DefaultRenderQueue
				);
				_defaultHillsLandmark[hill] = MaterialPool.MatFrom(
					path,
					ShaderDatabase.WorldOverlayTransparentLit,
					LandmarkColor,
					LandmarkRenderQueue
				);
			}
		}

		private static Bake BakeFor(BiomeDef biome)
		{
			if (_bakesByBiome.TryGetValue(biome, out Bake cached))
			{
				return cached;
			}

			Bake bake = BuildBake(biome);
			_bakesByBiome[biome] = bake;
			return bake;
		}

		private static object ExtFor(BiomeDef biome)
		{
			if (_extByBiome.TryGetValue(biome, out object cached))
			{
				return cached;
			}

			object ext = null;
			try
			{
				ext = _getModExtension.Invoke(biome, null);
			}
			catch (Exception ex)
			{
				WarnOnce("ReGrowth beautification: GetModExtension failed for " + biome.defName + ": " + ex.Message);
			}

			_extByBiome[biome] = ext;
			return ext;
		}

		private static Bake BuildBake(BiomeDef biome)
		{
			object ext = ExtFor(biome);
			if (ext == null)
			{
				return null;
			}

			var bake = new Bake
			{
				UniqueHills = BoolField(ext, "uniqueHills"),
				Forested = BoolField(ext, "forested"),
				MaterialLayer = IntField(ext, "materialLayer"),
				MaterialSizeMultiplier = FloatField(ext, "materialSizeMultiplier"),
				SnowpilesBelow = FloatField(ext, "snowpilesBelow"),
				ForestSnowyBelow = FloatField(ext, "forestSnowyBelow"),
				ForestSparseBelow = FloatField(ext, "forestSparseBelow"),
				ForestDenseAbove = FloatField(ext, "forestDenseAbove"),
			};

			float baseMultiplier = bake.MaterialSizeMultiplier;
			bake.HillSize[2] = baseMultiplier * FloatField(ext, "smallHillSizeMultiplier");
			bake.HillSize[3] = baseMultiplier * FloatField(ext, "largeHillSizeMultiplier");
			bake.HillSize[4] = baseMultiplier * FloatField(ext, "mountainSizeMultiplier");
			bake.HillSize[5] = baseMultiplier * FloatField(ext, "impassableSizeMultiplier");
			bake.HillSize[1] = baseMultiplier;

			bake.Semi[4] = FloatField(ext, "mountainsSemiSnowyBelow");
			bake.Snowy[4] = FloatField(ext, "mountainsSnowyBelow");
			bake.Very[4] = FloatField(ext, "mountainsVerySnowyBelow");
			bake.Fully[4] = FloatField(ext, "mountainsFullySnowyBelow");
			bake.Semi[5] = FloatField(ext, "impassableSemiSnowyBelow");
			bake.Snowy[5] = FloatField(ext, "impassableSnowyBelow");
			bake.Very[5] = FloatField(ext, "impassableVerySnowyBelow");
			bake.Fully[5] = FloatField(ext, "impassableFullySnowyBelow");

			string hillsPrefix = "WorldMaterials/BiomesKit/" + biome.defName + "/Hills/";
			for (int hill = 2; hill <= 5; hill++)
			{
				int bands = hill <= 3 ? 2 : 5;
				for (int band = 0; band < bands; band++)
				{
					string path = HillsPath(hillsPrefix, hill, band);
					if (path == null)
					{
						continue;
					}

					if (ContentFinder<Texture2D>.Get(path, false) == null)
					{
						path = DefaultHillPath(hill);
					}

					if (path == null || ContentFinder<Texture2D>.Get(path, false) == null)
					{
						continue;
					}

					int index = (hill - 1) * 5 + band;
					bake.HillsNormal[index] = MaterialPool.MatFrom(
						path,
						ShaderDatabase.WorldOverlayTransparentLit,
						Color.white,
						bake.MaterialLayer
					);
					bake.HillsLandmark[index] = MaterialPool.MatFrom(
						path,
						ShaderDatabase.WorldOverlayTransparentLit,
						LandmarkColor,
						LandmarkRenderQueue
					);
				}
			}

			string forestPrefix = "WorldMaterials/BiomesKit/" + biome.defName + "/Forest/Forest_";
			for (int snowy = 0; snowy < 2; snowy++)
			{
				for (int rain = 0; rain < 3; rain++)
				{
					string path = forestPrefix;
					bool suffixed = false;
					if (snowy == 1)
					{
						path += "Snowy";
						suffixed = true;
					}

					if (rain == 1)
					{
						path += "Sparse";
						suffixed = true;
					}
					else if (rain == 2)
					{
						path += "Dense";
						suffixed = true;
					}

					if (!suffixed)
					{
						path = path.TrimEnd('_');
					}

					if (ContentFinder<Texture2D>.Get(path, false) == null)
					{
						continue;
					}

					bake.Forest[snowy * 3 + rain] = MaterialPool.MatFrom(
						path,
						ShaderDatabase.WorldOverlayTransparentLit,
						bake.MaterialLayer
					);
				}
			}

			string overlayPath = StringField(ext, "materialPath");
			bake.HasOverlay = overlayPath != null && overlayPath != NotAnOverlay;
			if (bake.HasOverlay)
			{
				bake.Overlay = MaterialPool.MatFrom(
					overlayPath!,
					ShaderDatabase.WorldOverlayTransparentLit,
					bake.MaterialLayer
				);
			}

			return bake;
		}

		/// <summary>Rebuilds the path the original concatenates, for one (hilliness, snow band) pair.</summary>
		private static string HillsPath(string prefix, int hill, int band)
		{
			switch (hill)
			{
				case 2:
				return prefix + (band == 0 ? "SmallSnowpiles" : "SmallHills");
				case 3:
				return prefix + (band == 0 ? "LargeSnowpiles" : "LargeHills");
				case 4:
				return prefix + "Mountains" + _snowSuffixByBand[band];
				case 5:
				return prefix + "Impassable" + _snowSuffixByBand[band];
				default:
				return null;
			}
		}

		private static string DefaultHillPath(int hill)
		{
			return hill switch
			{
				2 => "WorldMaterials/BiomesKit/Default/Hills/SmallHills",
				3 => "WorldMaterials/BiomesKit/Default/Hills/LargeHills",
				4 => "WorldMaterials/BiomesKit/Default/Hills/Mountains",
				5 => "WorldMaterials/BiomesKit/Default/Hills/Impassable",
				_ => null,
			};
		}

		/// <summary>
		/// Band index for the hills branch. 2/3 hills have a single snowpiles threshold, 4/5 have the
		/// four-step SnowSuffix ladder. Returns -1 for hilliness values the original draws nothing for.
		/// </summary>
		private static int HillsBand(Bake bake, int hill, float temperature)
		{
			switch (hill)
			{
				case 2:
				case 3:
				return temperature < bake.SnowpilesBelow ? 0 : 1;
				case 4:
				return SnowBand(temperature, bake.Semi[4], bake.Snowy[4], bake.Very[4], bake.Fully[4]);
				case 5:
				return SnowBand(temperature, bake.Semi[5], bake.Snowy[5], bake.Very[5], bake.Fully[5]);
				default:
				return -1;
			}
		}

		private static int SnowBand(float temperature, float semi, float snowy, float verySnowy, float fullySnowy)
		{
			if (temperature < fullySnowy)
			{
				return 4;
			}

			if (temperature < verySnowy)
			{
				return 3;
			}

			if (temperature < snowy)
			{
				return 2;
			}

			if (temperature < semi)
			{
				return 1;
			}

			return 0;
		}

		private static bool ReadFallbackSetting()
		{
			try
			{
				object value = _defaultFallback.GetValue(null);
				return value is bool flag && flag;
			}
			catch
			{
				return false;
			}
		}

		private static bool BoolField(object ext, string name)
		{
			return _extFields.TryGetValue(name, out FieldInfo field) && field.GetValue(ext) is bool value && value;
		}

		private static int IntField(object ext, string name)
		{
			return _extFields.TryGetValue(name, out FieldInfo field) && field.GetValue(ext) is int value ? value : 0;
		}

		private static float FloatField(object ext, string name)
		{
			if (!_extFields.TryGetValue(name, out FieldInfo field))
			{
				return 0f;
			}

			object value = field.GetValue(ext);
			return value is float f ? f : 0f;
		}

		private static string StringField(object ext, string name)
		{
			return _extFields.TryGetValue(name, out FieldInfo field) ? field.GetValue(ext) as string : null;
		}

		private sealed class Bake
		{
			internal bool UniqueHills;
			internal bool Forested;
			internal bool HasOverlay;
			internal int MaterialLayer;
			internal float MaterialSizeMultiplier;
			internal float SnowpilesBelow;
			internal float ForestSnowyBelow;
			internal float ForestSparseBelow;
			internal float ForestDenseAbove;

			/// <summary>Size multiplier per hilliness (1..5), already multiplied by materialSizeMultiplier.</summary>
			internal readonly float[] HillSize = new float[6];

			internal readonly float[] Semi = new float[6];
			internal readonly float[] Snowy = new float[6];
			internal readonly float[] Very = new float[6];
			internal readonly float[] Fully = new float[6];

			/// <summary>[(hilliness - 1) * 5 + snow band]. Null = the original draws nothing for that pair.</summary>
			internal readonly Material[] HillsNormal = new Material[30];
			internal readonly Material[] HillsLandmark = new Material[30];

			/// <summary>[snowy * 3 + rain state]. Null = texture missing, original skips the tile.</summary>
			internal readonly Material[] Forest = new Material[6];

			internal Material Overlay;
		}
	}
}
