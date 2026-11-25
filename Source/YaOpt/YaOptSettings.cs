using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using Verse;
using YaOpt.Helpers;
using YaOpt.Patches;
using YaOpt.Settings;

namespace YaOpt
{
	public class YaOptSettings : ModSettings
	{
		public OptimizationOption DebugOutput { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.DebugOutput",
			Desc = "YaOpt.Setting.Option.DebugOutput.Desc",
			Category = OptimizationCategory.Main,
			Flags = OptimizationFlags.IgnoreEnableAll | OptimizationFlags.IgnoreDisableAll | OptimizationFlags.NoSnapshot,
			Default = false
		};

		/// <summary>
		/// The native crash reporter, backed by YaOpt.NativeVEH.dll.
		/// </summary>
		/// <remarks>
		/// The VEH callback must be pure native code: on CoreCLR a managed callback (a marshaled
		/// delegate or an [UnmanagedCallersOnly] method) fail-fasts the process with
		/// ExecutionEngineException 0x80131506 when entered from a cooperative-mode thread, and a
		/// stack overflow is always exactly that. The shim records the fault site for native
		/// faults the CLR never surfaces to managed code - access violation, illegal instruction,
		/// stack overflow, in-page error, privileged instruction - to
		/// YaOpt_Crash_&lt;pid&gt;.log in the save data folder, then lets the CLR fail-fast
		/// proceed. CLR fail-fast (0xC0000409) is unreachable from user mode and still requires a
		/// dump. Source: Source/NativeVEH/YaOptVeh.c.
		/// </remarks>
		public OptimizationOption VectoredExceptionHandler { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.VectoredExceptionHandler",
			Desc = "YaOpt.Setting.Option.VectoredExceptionHandler.Desc",
			Category = OptimizationCategory.Main,
			Flags = OptimizationFlags.IgnoreEnableAll | OptimizationFlags.IgnoreDisableAll,
			FuncShow = (_) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		};

		/// <summary>
		/// Managed half of the crash report: appends the UnhandledException stack to the shared
		/// crash log and surfaces the previous run's report at startup.
		/// </summary>
		public OptimizationOption EnhancedCrashLog { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.EnhancedCrashLog",
			Desc = "YaOpt.Setting.Option.EnhancedCrashLog.Desc",
			Category = OptimizationCategory.Main,
			Flags = OptimizationFlags.IgnoreEnableAll | OptimizationFlags.IgnoreDisableAll
		};

		/// <summary>
		/// Redirects the process stderr to the managed crash log so runtime-fatal text survives
		/// an instant exit. Skipped when gpp is loaded or stderr is already owned by a file/console.
		/// </summary>
		public OptimizationOption FatalStderrCapture { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FatalStderrCapture",
			Desc = "YaOpt.Setting.Option.FatalStderrCapture.Desc",
			Category = OptimizationCategory.Main,
			Flags = OptimizationFlags.IgnoreEnableAll | OptimizationFlags.IgnoreDisableAll,
			FuncShow = (_) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		};

		/// <summary>
		/// Optimizes <see cref="Material.color"/> by caching the value in a managed container.
		/// Unity <see cref="Material.color"/> involves a managed-to-native transition,
		/// which creates significant overhead when called frequently.
		/// This option patchs the setter of <see cref="Material.color"/> to update the cache and
		/// the getter of <see cref="Material.GetColor(string)"/> to retrieve the value from cache.
		/// </summary>
		/// <seealso cref="Patches.UnityEngine_Material_GetColor"/>
		/// <seealso cref="Patches.UnityEngine_Material_SetColor"/>
		public OptimizationOption OptMaterialGetColor { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.MaterialGetColor",
			Desc = "YaOpt.Setting.Option.MaterialGetColor.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Optimizes <see cref="ColoredText.Resolve(TaggedString)"/> and <see cref="ColoredText.StripTags(string)"/>.
		/// Replaces <see cref="string.IndexOf(string)"/> (culture-sensitive) with <see cref="string.IndexOf(string, StringComparison)"/>
		/// using <see cref="StringComparison.Ordinal"/> to avoid unnecessary overhead during text tagging.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_ColoredText"/>
		public OptimizationOption OptColoredText { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ColoredText",
			Desc = "YaOpt.Setting.Option.ColoredText.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Caches pawn name labels and their measured widths for a short period.
		/// Vanilla rebuilds the label string and measures it at least twice per rendered pawn per frame.
		/// </summary>
		/// <seealso cref="Patches.GenMapUI_GetPawnLabel"/>
		/// <seealso cref="Patches.GenMapUI_GetPawnLabelNameWidth"/>
		public OptimizationOption OptPawnLabelCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PawnLabelCache",
			Desc = "YaOpt.Setting.Option.PawnLabelCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.PawnLabelCache.Compatibility",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Starts the render preparation for dynamic objects earlier.
		/// <br/>
		/// Details:
		/// <list type="bullet">
		/// <item>Removes <c>ComputeCulledThings</c>, <c>DynamicDrawPhase(DrawPhase.EnsureInitialized)</c>,
		///		and <c>PreDrawVisibleThings</c> from <c>DynamicDrawManager.DrawDynamicThings</c>.</item>
		/// <item>Runs camera culling (<c>ComputeCulledThings</c>) before <c>MapMeshDrawerUpdate_First</c>.</item>
		/// <item>Runs initialization and parallel preparation before <c>DrawMapMesh</c>.</item>
		/// </list>
		/// </summary>
		/// <seealso cref="Patches.Verse_DynamicDrawManager_ComputeCulledThings"/>
		/// <seealso cref="Patches.Verse_DynamicDrawManager_DrawDynamicThings"/>
		/// <seealso cref="Patches.Verse_DynamicDrawManager_PreDrawVisibleThings"/>
		/// <seealso cref="Patches.Verse_Map_MapUpdate"/>
		/// <seealso cref="Patches.YaOpt_Helpers_ParallelPreDrawHelper_StartPreDrawJob"/>
		/// <seealso cref="Patches.YaOpt_Helpers_ParallelPreDrawHelper_StartCullingJob"/>
		public OptimizationOption OptEarlyRenderPrepare { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.EarlyRenderPrepare",
			Desc = "YaOpt.Setting.Option.EarlyRenderPrepare.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.EarlyRenderPrepare.Compatibility",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Optimizes the batch size for parallel render preparation.
		/// Vanilla uses a large batch size for parallel render preparation.
		/// However, rendering costs vary significantly between objects (e.g., pawns vs. items),
		/// and objects are often clustered (e.g., raids).
		/// This can lead to thread imbalance where some threads finish early while others are stuck with heavy batches.
		/// This optimization reduces the batch size, allowing the work-stealing algorithm to distribute the load more evenly.
		/// </summary>
		/// <seealso cref="Patches.Verse_DynamicDrawManager_PreDrawVisibleThings"/>
		public OptimizationOption OptPrepareBatchCount { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PrepareBatchCount",
			Desc = "YaOpt.Setting.Option.PrepareBatchCount.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Moves material parameter updates (e.g., via <see cref="PawnRenderNodeWorker"/>)
		/// to the parallel rendering preparation phase.
		/// Vanilla performs these updates on the main thread, but they are generally thread-safe.
		/// If a mod overrides the update logic in a way that might not be thread-safe,
		/// YaOpt will detect this and falls back to the main thread for those specific objects.
		/// </summary>
		/// <seealso cref="Patches.Verse_PawnRenderNodeWorker_GetMaterialPropertyBlock"/>
		/// <seealso cref="Patches.Verse_PawnRenderNodeWorker_PreDraw"/>
		/// <seealso cref="Patches.Verse_PawnRenderTree_ParallelPreDraw"/>
		public OptimizationOption OptParallelMaterialUpdate { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelMaterialUpdate",
			Desc = "YaOpt.Setting.Option.ParallelMaterialUpdate.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Optimizes the check for whether an object needs to regenerate render parameters.
		/// Vanilla checks this by recursively calling every child node in the render tree.
		/// Since 97% of render trees are shallow (less than or equal to 4 layers),
		/// this optimization inlines the check for the first 4 layers into a single function,
		/// avoiding virtual function call overhead.
		/// </summary>
		/// <seealso cref="Patches.Verse_PawnRenderNode_EnsureInitialized"/>
		/// <seealso cref="Patches.Verse_PawnRenderTree_ParallelPreDraw"/>
		public OptimizationOption OptFastRecacheRequested { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FastRecacheRequested",
			Desc = "YaOpt.Setting.Option.FastRecacheRequested.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FastRecacheRequested.Compatibility",
			Category = OptimizationCategory.Fps,
			FuncShow = (_) => ParallelPreDrawHelper.FastRecacheRequestedAvailable
		};

		/// <summary>
		/// Throttles the update frequency of the map mesh (terrain rendering).
		/// In vanilla, any change in snow or dust thickness triggers an immediate mesh update in the rendering frame,
		/// consuming significant resources during frequent changes (e.g., snowfall or melting).
		/// This optimization imposes a minimum real-time interval between updates.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_MapMeshDirty"/>
		public OptimizationOption OptMapMeshUpdateThrottle { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.MapMeshUpdateThrottle",
			Desc = "YaOpt.Setting.Option.MapMeshUpdateThrottle.Desc",
			Category = OptimizationCategory.Fps,
			FuncPostDraw = SettingsPanel.MapMeshUpdateThrottlePostDraw,
			FuncExposeData = (settings) =>
			{
				var interval = settings.MapMeshUpdateInterval;
				Scribe_Values.Look(ref interval, "OptMapMeshUpdateThrottle_Interval", 300);
				settings.MapMeshUpdateInterval = interval;
			}
		};

		public int MapMeshUpdateInterval
		{
			get => _mapMeshUpdateInterval;
			set => _mapMeshUpdateInterval = math.clamp(value, 100, 1000);
		}

		[Unsaved]
		private int _mapMeshUpdateInterval = 300;

		/// <summary>
		/// Fixes a bug where <see cref="PawnRenderNodeProperties.Worker"/>
		/// cache was not being used, causing slow initialization.
		/// </summary>
		/// <seealso cref="Patches.Verse_PawnRenderNodeProperties_Worker"/>
		public OptimizationOption OptPRNRWorker { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PRNRWorker",
			Desc = "YaOpt.Setting.Option.PRNRWorker.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.PRNRWorker.Compatibility",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Optimizes the initialization of <see cref="Graphic_Multi"/> by caching the textures
		/// for the four cardinal directions.
		/// Vanilla constructs texture paths via string concatenation (e.g. "_north", "_south")
		/// and performs a content lookup for every new graphic instance.
		/// This optimization caches the resolved textures based on the base path,
		/// eliminating the overhead of string manipulation and repeated asset lookups.
		/// </summary>
		/// <seealso cref="Patches.Verse_Graphic_Multi_Init"/>
		public OptimizationOption OptGraphicTextureCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GraphicTextureCache",
			Desc = "YaOpt.Setting.Option.GraphicTextureCache.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Caches texture resolution for world-map layers that repeatedly query the same paths.
		/// </summary>
		/// <seealso cref="Patches.Verse_ContentFinder_TextureGet"/>
		public OptimizationOption OptWorldMapTextureCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldMapTextureCache",
			Desc = "YaOpt.Setting.Option.WorldMapTextureCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.WorldMapTextureCache.Compatibility",
			SubCategory = "WorldMap",
			Category = OptimizationCategory.Misc,
		};

		/// <summary>
		/// Indexes world-layer submeshes by material to avoid a linear scan for every world tile.
		/// </summary>
		/// <seealso cref="Patches.Verse_WorldDrawLayerBase_GetSubMesh"/>
		public OptimizationOption OptWorldMapSubMeshCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldMapSubMeshCache",
			Desc = "YaOpt.Setting.Option.WorldMapSubMeshCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.WorldMapSubMeshCache.Compatibility",
			SubCategory = "WorldMap",
			Category = OptimizationCategory.Misc,
		};

		/// <summary>
		/// Short-circuits VFE Insectoids' world-layer territory lookup outside insect settlements.
		/// </summary>
		/// <seealso cref="Patches.VFEInsectoids_WorldDrawLayer_Insects_TryAddMeshForTile"/>
		public OptimizationOption OptWorldMapInsectLayer { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldMapInsectLayer",
			Desc = "YaOpt.Setting.Option.WorldMapInsectLayer.Desc",
			SubCategory = "WorldMap",
			Category = OptimizationCategory.Misc,
			RequiredMod = "oskarpotocki.vfe.insectoid2",
		};

		/// <summary>
		/// Bakes ReGrowth's world beautification paths and materials once per biome.
		/// </summary>
		/// <seealso cref="Patches.ReGrowthCore_WorldDrawLayer_Beautification_Regenerate"/>
		public OptimizationOption OptWorldMapRegrowth { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldMapRegrowth",
			Desc = "YaOpt.Setting.Option.WorldMapRegrowth.Desc",
			NoteStability = "YaOpt.Setting.Option.WorldMapRegrowth.Stability",
			SubCategory = "WorldMap",
			Category = OptimizationCategory.Misc,
			RequiredMod = "regrowth.botr.core",
		};

		/// <summary>
		/// Prefills Geological Landforms' world-tile cache in parallel before the layer draws.
		/// </summary>
		/// <remarks>
		/// This is a loading-only exception to the branch's single-threaded tick model. Versions that
		/// already provide <c>WorldTileInfo.WarmCache</c> are left untouched.
		/// </remarks>
		/// <seealso cref="Patches.GeologicalLandforms_WorldLayer_Landforms_Regenerate"/>
		public OptimizationOption OptWorldMapLandformsParallel { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldMapLandformsParallel",
			Desc = "YaOpt.Setting.Option.WorldMapLandformsParallel.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.WorldMapLandformsParallel.Compatibility",
			NoteMultithread = "YaOpt.Setting.Option.WorldMapLandformsParallel.Multithread",
			SubCategory = "WorldMap",
			Category = OptimizationCategory.Misc,
			Flags = OptimizationFlags.MultiplayerIncompatible,
			RequiredMod = "m00nl1ght.GeologicalLandforms",
		};

		/// <summary>
		/// Optimizes <see cref="SilhouetteUtility.GetCachedSilhouetteData"/>.
		/// Vanilla uses a struct as a dictionary key for silhouette caching but fails to implement <see cref="IEquatable{T}"/>.
		/// This forces the runtime to use <see cref="ValueType.Equals(object)"/>, causing boxing and reflection overhead for every lookup.
		/// This optimization replaces the cache key with a custom implementation that properly implements <see cref="IEquatable{T}"/> to eliminate this overhead.
		/// </summary>
		/// <seealso cref="Patches.Verse_SilhouetteUtility_GetCachedSilhouetteData"/>
		/// <seealso cref="Patches.Verse_SilhouetteUtility_NotifyGraphicDirty"/>
		public OptimizationOption OptSilhouette { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.Silhouette",
			Desc = "YaOpt.Setting.Option.Silhouette.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Reuses the visibility check of toggleable tabs within one frame.
		/// The UI reads <see cref="MainButtonWorker.Disabled"/> four to five times per event (the <c>Visible</c>
		/// reads included, since a tab that hides itself reports <c>!Disabled</c>) and runs several events per frame.
		/// While vanilla tabs cache this check, some modded tabs scan the map every time without caching.
		/// This optimization answers the later reads of the same frame from the first one, so nothing is
		/// delayed beyond that frame.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_ToggleTab"/>
		public OptimizationOption OptToggleTabCheck { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ToggleTabCheck",
			Desc = "YaOpt.Setting.Option.ToggleTabCheck.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Accelerates the matrix computation step in render preparation using Burst.
		/// Vanilla intended to use Burst for this computationally expensive step but failed due to an implementation oversight.
		/// This optimization rewrites the Burst implementation to make it functional.
		/// </summary>
		/// <seealso cref="Patches.Verse_PawnRenderTree_TryGetMatrix"/>
		public OptimizationOption OptComputeMatrixBurst { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ComputeMatrixBurst",
			Desc = "YaOpt.Setting.Option.ComputeMatrixBurst.Desc",
			Category = OptimizationCategory.Fps,
			Flags = OptimizationFlags.RequireBurst,
		};

		/// <summary>
		/// Accelerates <see cref="ThingWithComps.GetComp{T}"/>.
		/// Vanilla lookup is fast for existing components but very slow for missing ones,
		/// as it iterates through the entire component list.
		/// Unfortunately, checking if a component exists requires calling GetComp and waiting for a null result.
		/// This optimization rewrites the lookup mechanism to ensure fast failure for missing components.
		/// </summary>
		/// <seealso cref="MultiTargets_ThingGetComp"/>
		/// <seealso cref="Patches.Prepatch.Verse_ThingWithComps_GetComp"/>
		public OptimizationOption OptThingGetComp { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingGetComp",
			Desc = "YaOpt.Setting.Option.ThingGetComp.Desc",
			NotePrepatch = "YaOpt.Setting.Option.ThingGetComp.Prepatch",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.RequirePrepatcher
		};

		/// <summary>
		/// Throttles pawn meditation ticks from every tick to once every 100 ticks.
		/// Each throttled tick provides 100x the effect.
		/// Meditation can be performance-intensive,
		/// especially for tribal psycasters scanning for Anima grass every frame.
		/// Note: This has a minor gameplay impact. When a pawn stops meditating,
		/// any accrued progress between the 100-tick intervals is lost.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_JobDriver_Meditate_MeditationTick"/>
		public OptimizationOption OptMeditationTick { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.MeditationTick",
			Desc = "YaOpt.Setting.Option.MeditationTick.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.MeditationTick.Compatibility",
			Category = OptimizationCategory.Tps
		};

		/// <summary>
		/// Throttles bed usability check when pawns are lying down in their beds.
		/// This overlaps with the very same optimization from Performance Fish.
		/// Performance Fish throttles the check frequency to once every 64 ticks,
		/// and performs no check on unchecked ticks.
		/// YaOpt throttles the check frequency to once every 15 ticks,
		/// and performs a simple, quick check on unchecked ticks.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_Toils_Bed_BedNoLongerUsable"/>
		public OptimizationOption OptBedThrottle { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.BedThrottle",
			Desc = "YaOpt.Setting.Option.BedThrottle.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.BedThrottle.Compatibility",
			Category = OptimizationCategory.Tps
		};

		/// <summary>
		/// Dynamically throttles idle job checks based on the number of colonists.
		/// Includes optimizations for:
		/// 1. Lay down job override check (Toils_LayDown.LayDown)
		/// 2. Wander idle job duration (JobGiver_Wander)
		/// These significantly reduce the overhead of high-frequency checks when there are many colonists.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_Toils_LayDown_LayDown"/>
		/// <seealso cref="Patches.Verse_AI_JobGiver_Wander_TryGiveJob"/>
		public OptimizationOption OptIdleThrottle { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.IdleThrottle",
			Desc = "YaOpt.Setting.Option.IdleThrottle.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.IdleThrottle.Compatibility",
			Category = OptimizationCategory.Tps,
			FuncPostDraw = SettingsPanel.IdleThrottlePostDraw,
			FuncExposeData = (settings) =>
			{
				bool bValue = settings.IdleThrottleGetUpDynamic;
				Scribe_Values.Look(ref bValue, "OptIdleThrottle_GetUp_Dynamic", true);
				settings.IdleThrottleGetUpDynamic = bValue;
				bValue = settings.IdleThrottleStopWanderDynamic;
				Scribe_Values.Look(ref bValue, "OptIdleThrottle_StopWander_Dynamic", true);
				settings.IdleThrottleStopWanderDynamic = bValue;

				int iValue = settings.IdleThrottleGetUpIntervalMin;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_GetUp_MinInterval", 211);
				settings.IdleThrottleGetUpIntervalMin = iValue;
				iValue = settings.IdleThrottleGetUpIntervalMax;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_GetUp_MaxInterval", 600);
				settings.IdleThrottleGetUpIntervalMax = iValue;
				iValue = settings.IdleThrottleGetUpPeopleMin;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_GetUp_MinPeople", 3);
				settings.IdleThrottleGetUpPeopleMin = iValue;
				iValue = settings._idleThrottleGetUpPeopleMax;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_GetUp_MaxPeople", 15);
				settings._idleThrottleGetUpPeopleMax = iValue;
				iValue = settings._idleThrottleStopWanderIntervalMin;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_StopWander_MinInterval", 125);
				settings._idleThrottleStopWanderIntervalMin = iValue;
				iValue = settings.IdleThrottleStopWanderIntervalMax;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_StopWander_MaxInterval", 360);
				settings.IdleThrottleStopWanderIntervalMax = iValue;
				iValue = settings.IdleThrottleStopWanderPeopleMin;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_StopWander_MinPeople", 3);
				settings.IdleThrottleStopWanderPeopleMin = iValue;
				iValue = settings._idleThrottleStopWanderPeopleMax;
				Scribe_Values.Look(ref iValue, "OptIdleThrottle_StopWander_MaxPeople", 10);
				settings._idleThrottleStopWanderPeopleMax = iValue;
			}
		};

		[field: Unsaved]
		public bool IdleThrottleGetUpDynamic { get; set; } = true;

		public int IdleThrottleGetUpIntervalMin
		{
			get => _idleThrottleGetUpIntervalMin;
			set => _idleThrottleGetUpIntervalMin = math.clamp(value, 211, 1000);
		}

		public int IdleThrottleGetUpIntervalMax
		{
			get => _idleThrottleGetUpIntervalMax;
			set => _idleThrottleGetUpIntervalMax = math.clamp(value, 211, 1000);
		}

		public int IdleThrottleGetUpPeopleMin
		{
			get => _idleThrottleGetUpPeopleMin;
			set => _idleThrottleGetUpPeopleMin = math.clamp(value, 1, 20);
		}

		public int IdleThrottleGetUpPeopleMax
		{
			get => _idleThrottleGetUpPeopleMax;
			set => _idleThrottleGetUpPeopleMax = math.clamp(value, 1, 50);
		}

		[field: Unsaved]
		public bool IdleThrottleStopWanderDynamic { get; set; } = true;

		public int IdleThrottleStopWanderIntervalMin
		{
			get => _idleThrottleStopWanderIntervalMin;
			set => _idleThrottleStopWanderIntervalMin = math.clamp(value, 125, 1000);
		}

		public int IdleThrottleStopWanderIntervalMax
		{
			get => _idleThrottleStopWanderIntervalMax;
			set => _idleThrottleStopWanderIntervalMax = math.clamp(value, 125, 1000);
		}

		public int IdleThrottleStopWanderPeopleMin
		{
			get => _idleThrottleStopWanderPeopleMin;
			set => _idleThrottleStopWanderPeopleMin = math.clamp(value, 1, 20);
		}

		public int IdleThrottleStopWanderPeopleMax
		{
			get => _idleThrottleStopWanderPeopleMax;
			set => _idleThrottleStopWanderPeopleMax = math.clamp(value, 1, 50);
		}

		[Unsaved]
		private int _idleThrottleGetUpIntervalMin = 211;

		[Unsaved]
		private int _idleThrottleGetUpIntervalMax = 600;

		[Unsaved]
		private int _idleThrottleGetUpPeopleMin = 3;

		[Unsaved]
		private int _idleThrottleGetUpPeopleMax = 15;

		[Unsaved]
		private int _idleThrottleStopWanderIntervalMin = 125;

		[Unsaved]
		private int _idleThrottleStopWanderIntervalMax = 360;

		[Unsaved]
		private int _idleThrottleStopWanderPeopleMin = 3;

		[Unsaved]
		private int _idleThrottleStopWanderPeopleMax = 10;

		/// <summary>
		/// Extends the vanilla stat caching system to include <see cref="StatDefOf.ComfyTemperatureMin"/>,
		/// <see cref="StatDefOf.ComfyTemperatureMax"/>, and <see cref="StatDefOf.FilthRate"/>.
		/// Comfortable Temperature is updated every 20 ticks (and invalidated on apparel changes).
		/// Filth Rate is updated every 60 ticks.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_ComfortableTemperature"/>
		/// <seealso cref="Patches.MultiTargets_FilthRate"/>
		/// <seealso cref="Patches.RimWorld_Pawn_ApparelTracker_Notify_ApparelChanged"/>
		public OptimizationOption OptStatCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.StatCache",
			Desc = "YaOpt.Setting.Option.StatCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <see cref="GenLocalDate"/> lookups for the remainder of the current tick, per map.
		/// </summary>
		/// <remarks>
		/// Each lookup ends in <c>Find.WorldGrid.LongLatOf</c>, which rebuilds the tile centre from the
		/// planet mesh on every call and is not cached in 1.7. <c>SkyManager</c>, <c>WeatherWorker</c> and
		/// <c>GenCelestial</c> ask for the same map's date a dozen times per tick.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_GenLocalDate_DayPercent_Map"/>
		/// <seealso cref="Patches.RimWorld_GenLocalDate_DayTick_Map"/>
		public OptimizationOption OptLocalDateCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.LocalDateCache",
			Desc = "YaOpt.Setting.Option.LocalDateCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <see cref="RecipeDef.AllRecipeUsers"/> until the def list or the recipe's own user list
		/// changes.
		/// </summary>
		/// <seealso cref="Patches.Verse_RecipeDef_AllRecipeUsers"/>
		public OptimizationOption OptRecipeUsersCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RecipeUsersCache",
			Desc = "YaOpt.Setting.Option.RecipeUsersCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Memoises <see cref="ThingDef.BaseMass"/>, <see cref="ThingDef.BaseFlammability"/> and
		/// <see cref="ThingDef.BaseMaxHitPoints"/>.
		/// </summary>
		/// <seealso cref="Patches.Verse_ThingDef_BaseMass"/>
		/// <seealso cref="Patches.Verse_ThingDef_BaseFlammability"/>
		/// <seealso cref="Patches.Verse_ThingDef_BaseMaxHitPoints"/>
		public OptimizationOption OptThingDefBaseStatCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingDefBaseStatCache",
			Desc = "YaOpt.Setting.Option.ThingDefBaseStatCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.ThingDefBaseStatCache.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Skips the rebuild of <c>WorldPawns.AllPawnsAlive</c> and <c>WorldPawns.AllPawnsAliveOrDead</c>
		/// while the backing pawn sets are unchanged.
		/// </summary>
		/// <remarks>
		/// Vanilla clears the result list and bulk-copies both backing <see cref="HashSet{T}"/>s into it
		/// on every access. The patch returns vanilla's own result field, so callers see the same list
		/// identity and no extra memory is retained.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_Planet_WorldPawns_AllPawnsAlive"/>
		public OptimizationOption OptWorldPawnsListCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WorldPawnsListCache",
			Desc = "YaOpt.Setting.Option.WorldPawnsListCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <c>ThingFilter.AllStorableThingDefs</c>, a LINQ scan over the whole thing def database
		/// that <c>SetAllowAll</c> enumerates whenever a storage filter is reset.
		/// </summary>
		/// <seealso cref="Patches.Verse_ThingFilter_AllStorableThingDefs"/>
		public OptimizationOption OptStorableDefsCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.StorableDefsCache",
			Desc = "YaOpt.Setting.Option.StorableDefsCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <see cref="Room.ContainedAndAdjacentThings"/> until a region of the room or one of
		/// its thing lists changes.
		/// </summary>
		/// <remarks>
		/// Vanilla rebuilds the list, and de-duplicates it through a <see cref="HashSet{T}"/>, on every
		/// access. One room stats recompute reads it a dozen times through the stat and role workers.
		/// The cached contents are copied back into vanilla's own list, so callers see the same object
		/// and the same ordering.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Room_ContainedAndAdjacentThings_Cache"/>
		public OptimizationOption OptRoomThingsCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RoomThingsCache",
			Desc = "YaOpt.Setting.Option.RoomThingsCache.Desc",
			Category = OptimizationCategory.Tps,
			Default = true,
		};

		/// <summary>
		/// Limits how often a room recomputes its stats and role after something inside it changed.
		/// </summary>
		/// <remarks>
		/// Vanilla recomputes whenever the room is read while <c>statsAndRoleDirty</c> is set, and that
		/// flag is raised by every non-pawn thing spawned or despawned in the room. Each recompute runs
		/// every room stat and room role worker, several of which walk all cells of the room. Reads
		/// return the previous values for up to <see cref="Helpers.RoomContentCache.StatsUpdateInterval"/>
		/// ticks instead.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Room_Role_Throttle"/>
		/// <seealso cref="Patches.Verse_Room_GetStat_Throttle"/>
		public OptimizationOption OptRoomStatsThrottle { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RoomStatsThrottle",
			Desc = "YaOpt.Setting.Option.RoomStatsThrottle.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.RoomStatsThrottle.Compatibility",
			Category = OptimizationCategory.Tps,
			Default = true,
		};

		/// <summary>
		/// Caches <see cref="StatDefOf.MoveSpeed"/> for 20 ticks at the <see cref="Pawn.TicksPerMove"/>
		/// call site. Vanilla evaluates MoveSpeed once per cell walked and never reads the temporary
		/// stat cache for it (<c>cacheStaleAfterTicks</c> defaults to -1); MoveSpeed is also not
		/// <c>cacheable</c>, so the dictionary is never created. An XML patch turns cacheability on and
		/// this option switches the call site to a 20-tick interval.
		/// </summary>
		/// <remarks>
		/// Kept separate from <see cref="OptStatCache"/> on purpose: this one changes movement timing
		/// (up to 20 ticks of staleness), whereas OptStatCache only touches temperature and filth.
		/// Revenants are excluded because StatPart_RevenantSpeed reads the current tick.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_StatWorker_GetValue_MoveSpeedCache"/>
		/// <seealso cref="Patches.Xml.PatchOperationSettingGate"/>
		public OptimizationOption OptMoveSpeedStatCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.MoveSpeedStatCache",
			Desc = "YaOpt.Setting.Option.MoveSpeedStatCache.Desc",
			Category = OptimizationCategory.Tps,
			Default = true,
		};

		/// <summary>
		/// Introduces a restricted multi-threaded handling for pawn updates.
		/// It implements a multi-threaded job interruption predictor that checks if the current job
		/// might fail or be interrupted by emergency jobs (e.g. fleeing enemies) in the current frame.
		/// If the prediction passes (no interruption), the main thread skips the redundant check.
		/// Otherwise, the main thread performs the standard check.
		/// </summary>
		/// <remarks>
		/// Locked off on this branch: the option is listed in <see cref="LockedOffOptimizations"/>, so
		/// <see cref="OptimizationOption.Enabled"/> always returns false and every patch gated on it is
		/// skipped, together with <see cref="YaOptGlobal.NeedThreadSafe"/>. 1.7 is maintained
		/// single-threaded: <c>ParallelPawnTickManager</c> and <c>JobPredictor</c> are neither exercised
		/// nor tested here. Re-verify them in-game before removing the entry from the lock list.
		/// </remarks>
		/// <seealso cref="Patches.Verse_AI_JobDriver_DriverTick"/>
		/// <seealso cref="Patches.Verse_AI_Pawn_JobTracker_JobTrackerTickInterval"/>
		/// <seealso cref="Patches.Verse_TickList_Tick"/>
		/// <seealso cref="YaOptGlobal.NeedThreadSafe"/>
		public OptimizationOption OptParallelPawnTick { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelPawnTick",
			Desc = "YaOpt.Setting.Option.ParallelPawnTick.Desc",
			NoteStability = "YaOpt.Setting.Option.ParallelPawnTick.Stable",
			NoteCompatibility = "YaOpt.Setting.Option.ParallelPawnTick.Compatibility",
			NoteMultithread = "YaOpt.Setting.Option.ParallelPawnTick.Multithread",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.MultiplayerIncompatible,
			FuncPostDraw = SettingsPanel.ParallelPawnTickPostDraw,
			FuncExposeData = (settings) =>
			{
				var jobFailurePrediction = settings.ParallelPawnJobFailurePrediction;
				var constantJobPrediction = settings.ParallelPawnConstantJobPrediction;
				Scribe_Values.Look(ref jobFailurePrediction, "OptLazyTextureLoad_JobFailurePrediction", true);
				Scribe_Values.Look(ref constantJobPrediction, "OptLazyTextureLoad_ConstantJobPrediction", true);
				settings.ParallelPawnJobFailurePrediction = jobFailurePrediction;
				settings.ParallelPawnConstantJobPrediction = constantJobPrediction;
			}
		};

		[field: Unsaved]
		public bool ParallelPawnJobFailurePrediction { get; set; } = true;

		[field: Unsaved]
		public bool ParallelPawnConstantJobPrediction { get; set; } = true;

		/// <summary>
		/// Optimizes work giving by checking work priorities in parallel.
		/// Work giving is one of the most expensive operations. It iterates through a prioritized list of allowed works
		/// until a valid one is found. This optimization uses multiple threads to check this list.
		/// When a thread finds a valid work, it truncates the list (discarding lower priority works)
		/// and waits for threads checking higher priority works to finish.
		/// Finally, the valid work with the highest priority is selected.
		/// </summary>
		/// <remarks>
		/// Locked off on this branch: listed in <see cref="LockedOffOptimizations"/>, so
		/// <see cref="OptimizationOption.Enabled"/> is always false. This is the switch that keeps the
		/// entire <c>Helpers/ThreadLocal</c> and <c>Helpers/ThreadSafe</c> machinery dormant:
		/// <see cref="YaOptGlobal.NeedThreadSafe"/> is false without it, and every ThreadSafe/Reach patch
		/// gates on these two. <c>ParallelWorkGiver</c>, <c>JobPredictor</c> and the per-thread
		/// <c>Reachability</c> state are untested on 1.7 — re-test in-game before unlocking.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_JobGiver_Work_TryIssueJobPackage"/>
		/// <seealso cref="YaOptGlobal.NeedThreadSafe"/>
		public OptimizationOption OptParallelWorkGiver { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelWorkGiver",
			Desc = "YaOpt.Setting.Option.ParallelWorkGiver.Desc",
			NoteStability = "YaOpt.Setting.Option.ParallelWorkGiver.Stable",
			NoteCompatibility = "YaOpt.Setting.Option.ParallelWorkGiver.Compatibility",
			NoteMultithread = "YaOpt.Setting.Option.ParallelWorkGiver.Multithread",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.MultiplayerIncompatible,
		};

		/// <summary>
		/// Moves situational thought creation for the mood update onto worker threads.
		/// </summary>
		/// <remarks>
		/// Locked off on this branch: listed in <see cref="LockedOffOptimizations"/>, so
		/// <see cref="OptimizationOption.Enabled"/> is always false and
		/// <see cref="Helpers.ParallelThoughtUpdater"/> never runs. The option only exists to keep
		/// settings files from the 1.6 branch loadable; the code path itself is unmaintained on 1.7.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_SituationalThoughtHandler_UpdateAllMoodThoughts"/>
		public OptimizationOption OptParallelThoughtUpdate { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelThoughtUpdate",
			Desc = "YaOpt.Setting.Option.ParallelThoughtUpdate.Desc",
			NoteStability = "YaOpt.Setting.Option.ParallelThoughtUpdate.Stable",
			NoteMultithread = "YaOpt.Setting.Option.ParallelThoughtUpdate.Multithread",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.MultiplayerIncompatible,
		};

		/// <summary>
		/// Optimizes map post-tick processing by running independent updates in parallel.
		/// Supports steady environment effects, temp terrain and gas updates.
		/// </summary>
		/// <remarks>
		/// Locked off on this branch: listed in <see cref="LockedOffOptimizations"/>, so
		/// <see cref="OptimizationOption.Enabled"/> is always false. Consequently
		/// <see cref="Patches.Verse_Map_MapPostTick"/> is never applied (steady environment effects,
		/// temp terrain and gas keep ticking inline inside <c>Map.MapPostTick</c>) and
		/// <see cref="Patches.Verse_TickManager_DoSingleTick"/> skips its
		/// <c>ParallelMapTickManager.FinishPostMapTick</c> injection.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Map_MapPostTick"/>
		/// <seealso cref="Patches.Verse_TickManager_DoSingleTick"/>
		public OptimizationOption OptParallelPostMapTick { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelPostMapTick",
			Desc = "YaOpt.Setting.Option.ParallelPostMapTick.Desc",
			NoteStability = "YaOpt.Setting.Option.ParallelPostMapTick.Stable",
			NoteMultithread = "YaOpt.Setting.Option.ParallelPostMapTick.Multithread",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.MultiplayerIncompatible,
		};

		/// <summary>
		/// Optimizes the check for whether an Ideoligion allows a willing action.
		/// Vanilla iterates through all precepts to make this determination.
		/// This optimization creates a cache of precepts that impose restrictions whenever precepts are updated.
		/// When checking permissions, it only iterates through these cached restrictive precepts, ignoring irrelevant ones.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_Ideo_IdeoTick"/>
		/// <seealso cref="Patches.RimWorld_Ideo_MemberWillingToDo"/>
		/// <seealso cref="Patches.RimWorld_Ideo_RecachePrecepts"/>
		public OptimizationOption OptIdeoCheck { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.IdeoCheck",
			Desc = "YaOpt.Setting.Option.IdeoCheck.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Optimizes plant sway effect updates.
		/// Vanilla updates wind strength parameters for all plant materials every logic tick, which is unnecessary.
		/// This optimization restricts these updates to render frames only.
		/// </summary>
		/// <seealso cref="Patches.Verse_DynamicDrawManager_DrawDynamicThings"/>
		/// <seealso cref="Patches.Verse_WindManager_WindManagerTick"/>
		public OptimizationOption OptWindUpdate { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.WindUpdate",
			Desc = "YaOpt.Setting.Option.WindUpdate.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Optimizes the performance of removing things from the game.
		/// Vanilla maintains various lists (ListerThings) for object classification.
		/// Removing an object requires a linear search (O(N)) in each list.
		/// This optimization adds an index record to each object to track its position.
		/// Removal uses this index to directly access the object, swaps it with the last element (swap-remove),
		/// and updates the index of the moved element.
		/// This reduces complexity to O(1) at the cost of a negligible increase in memory usage.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_JobGiver_AIResurrectTarget_UpdateResurrectTarget"/>
		/// <seealso cref="Patches.Verse_ListerThings_Add"/>
		/// <seealso cref="Patches.Verse_ListerThings_Clear"/>
		/// <seealso cref="Patches.Verse_ListerThings_Constructor"/>
		/// <seealso cref="Patches.Verse_ListerThings_Contains"/>
		/// <seealso cref="Patches.Verse_ListerThings_Remove"/>
		/// <seealso cref="Patches.Verse_ThingDef_HasAssignableCompFrom"/>
		/// <seealso cref="Patches.Verse_TickManager_DoSingleTick"/>
		public OptimizationOption OptFastListerRemove { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FastListerRemove",
			Desc = "YaOpt.Setting.Option.FastListerRemove.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FastListerRemove.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Indexes <see cref="Map.spawnedThings"/> so removals during despawn are O(1).
		/// </summary>
		/// <seealso cref="Patches.Verse_ThingOwner_Remove"/>
		/// <seealso cref="Patches.Verse_ThingOwner_TryAdd"/>
		/// <seealso cref="Helpers.SpawnedThingsIndexer"/>
		public OptimizationOption OptFastSpawnedThingsRemove { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FastSpawnedThingsRemove",
			Desc = "YaOpt.Setting.Option.FastSpawnedThingsRemove.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Removes the global lock from <see cref="GenTypes.IsSealedWithCache"/>.
		/// </summary>
		/// <seealso cref="Patches.Verse_GenTypes_IsSealedWithCache"/>
		public OptimizationOption OptGenTypesLockFree { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GenTypesLockFree",
			Desc = "YaOpt.Setting.Option.GenTypesLockFree.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Optimizes <see cref="ConnectivitySource.UpdateIncrementally"/>.
		/// Vanilla re-clears a massive 160k-capacity HashSet within a loop when updating connectivity,
		/// causing significant overhead. This optimization replaces it with a smaller HashSet
		/// and hoists the clear operation outside the loop to improve performance and preserve deduplication.
		/// </summary>
		/// <seealso cref="Patches.Verse_ConnectivitySource_UpdateIncrementally"/>
		public OptimizationOption OptConnectivityUpdate { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ConnectivityUpdate",
			Desc = "YaOpt.Setting.Option.ConnectivityUpdate.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Reuses a static <see cref="HashSet{T}"/> in Humanoid Alien Races' need update postfix.
		/// HAR builds a two-element <c>HashSet&lt;Type&gt;</c> every time <see cref="Need.CurLevel"/>
		/// is written and only ever reads it, so the same set can be passed on every call.
		/// </summary>
		/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_HarmonyPatches_NeedLevelPostfix"/>
		public OptimizationOption OptHARNeedCondition { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.HARNeedCondition",
			Desc = "YaOpt.Setting.Option.HARNeedCondition.Desc",
			RequiredMod = "erdelf.HumanoidAlienRaces",
			Category = OptimizationCategory.Tps,
		};

		/// <seealso cref="Patches.Verse_GenTicks_GetCameraUpdateRate"/>
		public OptimizationOption OptCameraUpdateRate { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.CameraUpdateRate",
			Desc = "YaOpt.Setting.Option.CameraUpdateRate.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <seealso cref="Patches.RimWorld_GenHostility_HostileTo"/>
		/// <seealso cref="Patches.Verse_AI_AttackTargetFinder_BestAttackTarget"/>
		/// <seealso cref="Patches.Verse_AI_AttackTargetFinder_FriendlyFireConeTargetScoreOffset"/>
		/// <seealso cref="Patches.Verse_IntVec3_AngleFlat"/>
		public OptimizationOption OptAttackTargetFinder { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.AttackTargetFinder",
			Desc = "YaOpt.Setting.Option.AttackTargetFinder.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Indexes colonist buildings by <see cref="ThingDef"/> and answers the definition-based
		/// building queries from that map-local index.
		/// </summary>
		/// <remarks>
		/// The vanilla queries scan every colonist building each time. The index is built on the
		/// first query and maintained by <c>ListerBuildings.Add/Remove</c>. List results are copied
		/// into the vanilla result buffer, and empty results stay cached.
		/// </remarks>
		/// <seealso cref="Prepatch.PrepatcherFreePatches"/>
		/// <seealso cref="Patches.Verse_ListerBuildings_Add"/>
		/// <seealso cref="Patches.Verse_ListerBuildings_Remove"/>
		/// <seealso cref="Patches.Verse_ListerBuildings_Dispose"/>
		public OptimizationOption OptListerBuildingsCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ListerBuildingsCache",
			Desc = "YaOpt.Setting.Option.ListerBuildingsCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <see cref="ImmunityHandler.NeededImmunitiesNow"/> until the hediff list changes.
		/// </summary>
		/// <remarks>
		/// Vanilla rebuilds the needed-immunity list on every health tick for every pawn.
		/// The list only depends on the hediff defs, so the hediff list identity and
		/// <c>List&lt;T&gt;._version</c> are sufficient invalidation keys.
		/// </remarks>
		/// <seealso cref="Prepatch.PrepatcherFreePatches"/>
		/// <seealso cref="Helpers.ImmunityCache"/>
		public OptimizationOption OptImmunityCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ImmunityCache",
			Desc = "YaOpt.Setting.Option.ImmunityCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Publishes the invisibility state into an injected per-pawn field and answers the
		/// invisibility queries from it.
		/// </summary>
		/// <remarks>
		/// Vanilla answers <c>IsPsychologicallyInvisible</c>, <c>IsHiddenFromPlayer</c> and
		/// <c>GetAlpha</c> by scanning the pawn's hediff list; the renderer does that three to four
		/// times per visible pawn per frame, inside the parallel pre-draw job. The state is
		/// republished from the per-tick path vanilla already runs, so reading it costs one aligned
		/// 8-byte load. Pawns with several invisibility comps, or a fade duration that does not fit
		/// the packed field, keep the vanilla path.
		/// </remarks>
		/// <seealso cref="Prepatch.PrepatcherFields"/>
		/// <seealso cref="Helpers.PawnInvisibilityCache"/>
		/// <seealso cref="Patches.RimWorld_InvisibilityUtility_IsPsychologicallyInvisible"/>
		/// <seealso cref="Patches.RimWorld_InvisibilityUtility_IsHiddenFromPlayer"/>
		/// <seealso cref="Patches.RimWorld_InvisibilityUtility_GetAlpha"/>
		public OptimizationOption OptInvisibilityCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.InvisibilityCache",
			Desc = "YaOpt.Setting.Option.InvisibilityCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Extends the shambler's idle target-search interval when it has no target at all.
		/// </summary>
		/// <remarks>
		/// Vanilla searches every 900-1800 ticks even while the shambler is standing idle.
		/// Idle search is stretched to 15000-17499 ticks. Active shamblers, the 180-tick
		/// post-engagement grace and delayed alerts keep vanilla timing.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Hediff_Shambler_TickInterval"/>
		public OptimizationOption OptShamblerIdleThrottle { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ShamblerIdleThrottle",
			Desc = "YaOpt.Setting.Option.ShamblerIdleThrottle.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Reuses refuel fuel searches and supplies a maintained per-map refuel candidate list.
		/// </summary>
		/// <remarks>
		/// A work giver validates a refuel target with <c>CanRefuel</c> and then builds the job with
		/// <c>RefuelJob</c>; both run the same global closest-fuel search and, for atomic fueling,
		/// the same region traversal. Each result is reused once. The broad candidate list is
		/// maintained on spawn and despawn; vanilla validation checks all dynamic conditions.
		/// It is only used for the fallback after the normal local region scan.
		/// </remarks>
		/// <seealso cref="Prepatch.PrepatcherFreePatches"/>
		/// <seealso cref="Patches.RimWorld_CompRefuelable_PostSpawnSetup"/>
		/// <seealso cref="Patches.RimWorld_RefuelWorkGiverUtility_FindBestFuel"/>
		/// <seealso cref="Patches.RimWorld_RefuelWorkGiverUtility_FindAllFuel"/>
		public OptimizationOption OptRefuelSearchCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RefuelSearchCache",
			Desc = "YaOpt.Setting.Option.RefuelSearchCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Narrows death-thought witness and relation scans and deduplicates multi-cell witnesses.
		/// </summary>
		/// <remarks>
		/// Witnesses are collected from the twelve-cell radius around the victim instead of every
		/// living pawn, and relation candidates are materialized once. General lost memories still
		/// cover map, world and temporary pawns; relation-specific cleanup uses relation candidates.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_PawnDiedOrDownedThoughtsUtility_TryGiveThoughts"/>
		/// <seealso cref="Patches.RimWorld_PawnDiedOrDownedThoughtsUtility_RemoveLostThoughts"/>
		public OptimizationOption OptDeathThoughts { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.DeathThoughts",
			Desc = "YaOpt.Setting.Option.DeathThoughts.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.DeathThoughts.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Short-circuits turret target searches when no candidate can possibly be in range.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_Building_TurretGun_TryFindNewTarget"/>
		public OptimizationOption OptTurretTargetPrune { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.TurretTargetPrune",
			Desc = "YaOpt.Setting.Option.TurretTargetPrune.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.TurretTargetPrune.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <seealso cref="Patches.RimWorld_ChildcareUtility_FindAutofeedBaby"/>
		/// <seealso cref="Patches.RimWorld_ColonistBar_CheckRecacheEntries"/>
		/// <seealso cref="Patches.RimWorld_PawnsFinder_AllAlive_FreeColonistsAndPrisoners"/>
		/// <seealso cref="Patches.Verse_MapPawns_FreeColonistsAndPrisoners"/>
		/// <seealso cref="Patches.Verse_ThingOwnerUtility_GetAllThingsRecursively"/>
		public OptimizationOption OptGetMapPawns { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GetMapPawns",
			Desc = "YaOpt.Setting.Option.GetMapPawns.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.GetMapPawns.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <seealso cref="Patches.Verse_ThingOwnerUtility_AppendThingHoldersFromThings"/>
		public OptimizationOption OptAppendThingHolders { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.AppendThingHolders",
			Desc = "YaOpt.Setting.Option.AppendThingHolders.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Eliminates redundant CanTouchTargetFromValidCell checks in construction resource delivery.
		/// Vanilla calls CanTouchTargetFromValidCell before FirstBlockingThing and again inside CanConstruct.
		/// This optimization moves the CanTouchTargetFromValidCell check into FirstBlockingThing,
		/// so it is only evaluated when there is actually a blocking thing to handle.
		/// Also adds a per-thread cache to TryFindGoodAdjacentSpotToTouch for IsGoodDestinationFor
		/// and CanReach results, avoiding redundant pathfinding within the same tick.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_ConstructDeliverResources"/>
		/// <seealso cref="Patches.RCellFinder_TryFindGoodAdjacentSpotToTouch"/>
		public OptimizationOption OptConstructDeliverResources { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ConstructDeliverResources",
			Desc = "YaOpt.Setting.Option.ConstructDeliverResources.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Delays texture loading until they are first requested.
		/// Vanilla loads all mod textures into VRAM at startup.
		/// This optimization reduces startup time and initial VRAM usage,
		/// but may cause stutters when textures are loaded during gameplay.
		/// Note: Texture unloading is not implemented, so VRAM usage may increase over time.
		/// </summary>
		/// <seealso cref="Verse_ContentFinder_Get"/>
		/// <seealso cref="Patches.Early.MultiTargets_PatchOperationMulti"/>
		/// <seealso cref="Patches.Early.MultiTargets_PatchOperationSingle"/>
		/// <seealso cref="Patches.Early.Verse_ModContentLoader_LoadTexture"/>
		public OptimizationOption OptLazyTextureLoad { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.LazyTextureLoad",
			Desc = "YaOpt.Setting.Option.LazyTextureLoad.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.LazyTextureLoad.Compatibility",
			NotePrepatch = "YaOpt.Setting.Option.LazyTextureLoad.Prepatch",
			Category = OptimizationCategory.Misc,
			Flags = OptimizationFlags.RequirePrepatcher,
			FuncPostDraw = SettingsPanel.LazyTextureLoadPostDraw,
			FuncExposeData = (settings) =>
			{
				var ddsOnly = settings.LazyTextureLoadDdsOnly;
				var radical = settings.LazyTextureLoadRadical;
				Scribe_Values.Look(ref ddsOnly, "OptLazyTextureLoad_DdsOnly", true);
				Scribe_Values.Look(ref radical, "OptLazyTextureLoad_Radical", false);
				settings.LazyTextureLoadDdsOnly = ddsOnly;
				settings.LazyTextureLoadRadical = radical;
			}
		};

		[field: Unsaved]
		public bool LazyTextureLoadDdsOnly { get; set; } = true;

		[field: Unsaved]
		public bool LazyTextureLoadRadical { get; set; } = false;

		/// <summary>
		/// Optimizes XML patch operations by simplifying common XPath expressions.
		/// If an XPath expression follows a simple pattern like <c>Defs/DefType[defName="DefName"]</c>,
		/// this optimization replaces the complex XPath evaluation with a faster, direct node lookup.
		/// This significantly improves game startup time, especially with many mods.
		/// </summary>
		/// <seealso cref="Patches.Early.MultiTargets_PatchOperationMulti"/>
		/// <seealso cref="Patches.Early.MultiTargets_PatchOperationSingle"/>
		/// <seealso cref="Patches.Early.Verse_LoadedModManager_ApplyPatches"/>
		public OptimizationOption OptFastPatchOperation { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FastPatchOperation",
			Desc = "YaOpt.Setting.Option.FastPatchOperation.Desc",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Optimizes the translation injection process during game startup.
		/// When injecting translation data into XML Defs, the game checks for errors (e.g. duplicate injections).
		/// Vanilla uses an O(N^2) nested loop for this check, which is very slow with large numbers of Defs.
		/// This optimization replaces the inner loop with a hash lookup, reducing the complexity to O(N).
		/// This speeds up game startup, especially for non-English languages.
		/// </summary>
		/// <seealso cref="Patches.Early.Verse_DefInjectionPackage_InjectIntoDefs"/>
		/// <seealso cref="Patches.Early.Verse_DefInjectionPackage_SetDefFieldAtPath"/>
		public OptimizationOption OptFastTranslationInjection { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FastTranslationInjection",
			Desc = "YaOpt.Setting.Option.FastTranslationInjection.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FastTranslationInjection.Compatibility",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Caches type information at startup to accelerate Harmony type retrieval.
		/// This speeds up Harmony patch processing for some mods, improving game startup time in modded environments.
		/// It has no effect in a vanilla environment.
		/// </summary>
		/// <seealso cref="Patches.Early.HarmonyLib_AccessTools_TypeByName"/>
		/// <seealso cref="Patches.Early.System_Reflection_RuntimeAssembly_FullName"/>
		public OptimizationOption OptRuntimeInfoCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RuntimeInfoCache",
			Desc = "YaOpt.Setting.Option.RuntimeInfoCache.Desc",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Fixes a vanilla bug where texture atlases smaller than 512x512 have Mipmaps enabled but not populated.
		/// This causes visual corruption (colored stripes) when zooming out on objects using these atlases.
		/// The issue often affects scatterable items (e.g., pebbles) from high-res texture mods.
		/// Vanilla avoids this by using known-good built-in atlases, and large modlists avoid it by filling the atlas to >512x512.
		/// The bug only manifests in intermediate-sized modlists where the atlas size falls into the problematic range.
		/// </summary>
		/// <seealso cref="Patches.Early.MultiTargets_CalcRectsForAtlas"/>
		public OptimizationOption OptFixTextureAtlas { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FixTextureAtlas",
			Desc = "YaOpt.Setting.Option.FixTextureAtlas.Desc",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Optimizations that are unavailable in RimWorld 1.7.
		/// </summary>
		/// <remarks>
		/// Multithreading and mod-compatibility optimizations are retired. The remaining entries
		/// duplicate behavior now built into 1.7, depend on the removed Burst runtime, depend on
		/// the engine texture pipeline that 1.7 replaced, or rely on pre-1.7 IL.
		/// </remarks>
		private static readonly HashSet<string> LockedOffOptimizations = new HashSet<string>(StringComparer.Ordinal)
		{
			// Multithreading
			nameof(OptParallelPawnTick),
			nameof(OptParallelWorkGiver),
			nameof(OptParallelThoughtUpdate),
			nameof(OptParallelPostMapTick),
			nameof(OptEarlyRenderPrepare),
			nameof(OptPrepareBatchCount),
			nameof(OptParallelMaterialUpdate),

			// Unity Burst is a non-functional stub in 1.7.
			nameof(OptComputeMatrixBurst),

			// Superseded by vanilla 1.7 or incompatible with the 1.7 engine.
			nameof(OptThingGetComp),
			nameof(OptMaterialGetColor),
			nameof(OptAppendThingHolders),
			nameof(OptFixTextureAtlas),
			nameof(OptRuntimeInfoCache),
			nameof(OptLazyTextureLoad),

			// Other-mod compatibility submodules
			"OptCELineOfSightBurst",
			"OptFAParallelUpdate",
			"OptFADeLinq",
			"OptFADeLinqBurst",
			"OptFAAnimCache",
			"OptFAPawnSpawn",
			"OptFATextureCache",
			"OptHARDeLinq",
			"OptHARTextureCache",
			"OptVEFMemoryLeakFix"
		};

		private static void ApplyPlatformLock(OptimizationOption option)
		{
			if (!LockedOffOptimizations.Contains(option.SettingId))
				return;
			option.Flags |= OptimizationFlags.LockedOff;
			option.Default = false;
		}

		[Unsaved]
		private readonly List<OptimizationOption> _allOptimizations;

		[Unsaved]
		private readonly SettingsPanel _settingsPanel;

		public IReadOnlyList<OptimizationOption> AllOptimizations => _allOptimizations.AsReadOnly();

		public bool DebugLogging
		{
			set => DebugOutput.Enabled = value;
			get => DebugOutput.Enabled;
		}

		public YaOptSettings()
		{
			_allOptimizations = new List<OptimizationOption>();
			var props = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (var propertyInfo in props)
			{
				if (typeof(OptimizationOption).IsAssignableFrom(propertyInfo.PropertyType))
				{
					if (!(propertyInfo.GetValue(this) is OptimizationOption option))
					{
						// Should never happen
						continue;
					}
					_allOptimizations.Add(option);
					if (string.IsNullOrWhiteSpace(option.SettingId))
						option.SettingId = propertyInfo.Name;
					ApplyPlatformLock(option);
				}
			}

			foreach (var subMod in YaOptGlobal.SubMods)
			{
				try
				{
					var subModOptions = subMod.OnCreateSettings();
					foreach (var option in subModOptions)
					{
						_allOptimizations.Add(option);
						ApplyPlatformLock(option);
					}
				}
				catch (Exception ex)
				{
					YaOptMod.Error($"Failed to run OnCreateSettings for {subMod.GetType().FullName}\n{ex}");
				}
			}

			_allOptimizations.Sort((a, b) =>
			{
				var i = a.Category.CompareTo(b.Category);
				if (i == 0)
				{
					// Don't use translated text, so that the layout will be the same in any language
					var subCatA = !string.IsNullOrWhiteSpace(a.SubCategory) ? a.SubCategory : null;
					var subCatB = !string.IsNullOrWhiteSpace(b.SubCategory) ? b.SubCategory : null;
					i = string.Compare(subCatA, subCatB, StringComparison.Ordinal);
					if (i == 0)
					{
						i = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
					}
				}
				return i;
			});

			_settingsPanel = new SettingsPanel(this);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (YaOptGlobal.IsLibraryLoaded && Scribe.mode == LoadSaveMode.Saving)
			{
				ValidateOptions(true);
			}
			foreach (var option in _allOptimizations)
			{
				if ((option.Flags & OptimizationFlags.DontSave) == 0)
				{
					Scribe_Values.Look(ref option._enabled, option.SettingId, option.Default);
					if (option.FuncExposeData != null)
						option.FuncExposeData(this);
				}
			}
			if (YaOptGlobal.IsLibraryLoaded && Scribe.mode == LoadSaveMode.LoadingVars)
			{
				ValidateOptions(true);
			}
		}

		public void ValidateOptions(bool printError, bool translateMessage = true)
		{
			foreach (var option in _allOptimizations)
			{
				option.Validate(false, printError, translateMessage, out _);
			}
		}

		public void DoSettingsWindowContents(Rect inRect)
		{
			_settingsPanel.Draw(inRect);
		}
	}
}
