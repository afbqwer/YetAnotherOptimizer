using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using Verse;
using YaOpt.Defines;
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptMaterialGetColor { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.MaterialGetColor",
			Desc = "YaOpt.Setting.Option.MaterialGetColor.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Replaces the glow combine pass with a section-indexed implementation.
		/// </summary>
		/// <remarks>
		/// Vanilla re-evaluates every light for every dirty cell, so a single light-blocking wall can
		/// trigger millions of rect tests. The replacement keeps a per-section light index and a
		/// compact dirty-cell list, reducing per-cell candidates to the handful of lights that overlap
		/// the cell's 17x17 section. Accumulation order is preserved, so light colors are unchanged.
		/// </remarks>
		/// <seealso cref="Patches.Verse_GlowGrid_GlowGridUpdate_First"/>
		/// <seealso cref="Patches.Verse_GlowGrid_DirtyCell"/>
		public OptimizationOption OptGlowGridCombine { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GlowGridCombine",
			Desc = "YaOpt.Setting.Option.GlowGridCombine.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Replaces the gas grid's fixed-size random-cell sweeps with a bitmap-driven skip.
		/// </summary>
		/// <remarks>
		/// Vanilla processes <c>area/64</c> dissipation positions and <c>area/32</c> diffusion
		/// positions every tick whether or not the map holds any gas. The replacement keeps a
		/// one-bit-per-cell occupancy bitmap in <c>cellsInRandomOrder</c> order, so the same
		/// positions are covered per tick but empty 64-cell buckets cost a single word test.
		/// </remarks>
		/// <seealso cref="Patches.Verse_GasGrid_Tick"/>
		/// <seealso cref="Patches.Verse_GasGrid_SetDirect_Densities"/>
		/// <seealso cref="Patches.Verse_GasGrid_SetDirect_Packed"/>
		public OptimizationOption OptGasGridTick { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GasGridTick",
			Desc = "YaOpt.Setting.Option.GasGridTick.Desc",
			Category = OptimizationCategory.Tps
		};

		/// <summary>
		/// Ticks flecks in place instead of copying each struct out of the list and writing it back.
		/// </summary>
		/// <remarks>
		/// Vanilla <c>FleckSystemBase&lt;T&gt;.Tick</c> performs a full struct copy on read and another on
		/// write for every fleck every tick (about 160 B for <see cref="Verse.FleckStatic"/> and 270 B for
		/// <see cref="Verse.FleckThrown"/>). The replacement iterates the same list through a span, so the
		/// constrained <c>TimeInterval</c> call mutates the element directly. Fleck physics, lifetimes and
		/// removal order are unchanged.
		/// </remarks>
		/// <seealso cref="Patches.Verse_FleckManager_FleckManagerTick"/>
		public OptimizationOption OptFleckTickLoop { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FleckTickLoop",
			Desc = "YaOpt.Setting.Option.FleckTickLoop.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FleckTickLoop.Compatibility",
			Category = OptimizationCategory.Tps
		};

		/// <summary>
		/// Draws unrotated flecks without building a rotation quaternion.
		/// </summary>
		/// <remarks>
		/// <c>Graphic_Fleck.DrawFleck</c> always calls <c>Quaternion.AngleAxis</c> (one sin/cos pair).
		/// Static and splash flecks never rotate, and thrown flecks without a <c>rotationRate</c> stay at
		/// zero, so most draws can use the identity quaternion and produce the same matrix.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Graphic_Fleck_DrawFleck"/>
		public OptimizationOption OptFleckFastDraw { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FleckFastDraw",
			Desc = "YaOpt.Setting.Option.FleckFastDraw.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Builds a new fleck in place inside its system's list instead of copying it in afterwards.
		/// </summary>
		/// <remarks>
		/// Vanilla constructs each fleck as a local, runs <c>Setup</c> there and then appends a full copy
		/// of the 160-270 B struct. Growing the list first and running <c>Setup</c> on the new element
		/// removes that copy. Fleck data and ordering are unchanged.
		/// </remarks>
		/// <seealso cref="Patches.Verse_FleckManager_CreateFleck"/>
		public OptimizationOption OptFleckCreateInPlace { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FleckCreateInPlace",
			Desc = "YaOpt.Setting.Option.FleckCreateInPlace.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FleckCreateInPlace.Compatibility",
			Category = OptimizationCategory.Tps
		};

		/// <summary>
		/// Enables GPU instancing on fleck defs whose shader can consume per-instance data.
		/// </summary>
		/// <remarks>
		/// Vanilla instances only the defs that set <c>renderInstanced</c>, but whether that works depends
		/// on the shader declaring the <c>_InstanceTransforms</c> and <c>_Color</c> storage buffers that
		/// <c>DrawBatch.Flush</c> binds. This scans every <see cref="Verse.FleckDef"/>, keeps plain
		/// <c>Graphic_Fleck</c> graphics only (the pulse/splash graphics use a per-fleck property block and
		/// can never batch), and flips <c>renderInstanced</c> on where the material layout declares both
		/// buffers. Off by default because it changes render submission for defs vanilla left on the
		/// per-object path.
		/// </remarks>
		/// <seealso cref="Patches.Verse_FleckManager_Constructor"/>
		public OptimizationOption OptFleckAutoInstancing { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FleckAutoInstancing",
			Desc = "YaOpt.Setting.Option.FleckAutoInstancing.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.FleckAutoInstancing.Compatibility",
			Category = OptimizationCategory.Fps,
			Default = false
		};

		/// <summary>
		/// Removes the per-sound LINQ allocations and the O(n*m) cleanup pass from the one-shot manager.
		/// </summary>
		/// <remarks>
		/// <c>CanAddPlayingOneShot</c> and <c>TryAddPlayingOneShot</c> count matching samples through
		/// <c>Where(...).Count()</c> on every one-shot the game starts, and
		/// <c>SampleOneShotManagerUpdate</c> removes finished samples with
		/// <c>RemoveAll(s =&gt; cleanupList.Contains(s))</c> every frame. The replacements are direct scans
		/// and an in-place compaction with identical decisions and ordering.
		/// </remarks>
		/// <seealso cref="Patches.Verse_Sound_SampleOneShotManager_CanAddPlayingOneShot"/>
		/// <seealso cref="Patches.Verse_Sound_SampleOneShotManager_TryAddPlayingOneShot"/>
		/// <seealso cref="Patches.Verse_Sound_SampleOneShotManager_SampleOneShotManagerUpdate"/>
		public OptimizationOption OptSoundOneShotManager { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.SoundOneShotManager",
			Desc = "YaOpt.Setting.Option.SoundOneShotManager.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.SoundOneShotManager.Compatibility",
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptEarlyRenderPrepare { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.EarlyRenderPrepare",
			Desc = "YaOpt.Setting.Option.EarlyRenderPrepare.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.EarlyRenderPrepare.Compatibility",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptPrepareBatchCount { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PrepareBatchCount",
			Desc = "YaOpt.Setting.Option.PrepareBatchCount.Desc",
			Category = OptimizationCategory.Fps
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptComputeMatrixBurst { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ComputeMatrixBurst",
			Desc = "YaOpt.Setting.Option.ComputeMatrixBurst.Desc",
			Category = OptimizationCategory.Fps,
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptThingGetComp { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingGetComp",
			Desc = "YaOpt.Setting.Option.ThingGetComp.Desc",
			Category = OptimizationCategory.Tps
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
		/// Builds one reverse index for the def-wide recipe and research lookups once defs are
		/// loaded, so <see cref="ThingDef.AllRecipes"/>, <see cref="RecipeDef.AllRecipeUsers"/>,
		/// <see cref="ResearchProjectDef.UnlockedDefs"/> and the research prerequisite helpers stop
		/// rescanning the whole def database.
		/// </summary>
		/// <remarks>
		/// The index is built before the first <c>[StaticConstructorOnStartup]</c> constructor, so
		/// mods that query these lists during startup also benefit. Vanilla's own per-def caches are
		/// still filled with fresh copies, and the index is dropped when play data is torn down.
		/// </remarks>
		/// <seealso cref="Helpers.DefLookupIndex"/>
		/// <seealso cref="Patches.Early.Verse_ThingDef_AllRecipes"/>
		/// <seealso cref="Patches.Early.Verse_ResearchPrerequisitesUtility_ComputeResearchPrerequisites"/>
		public OptimizationOption OptDefLookupCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.DefLookupCache",
			Desc = "YaOpt.Setting.Option.DefLookupCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.DefLookupCache.Compatibility",
			SubCategory = "Startup",
			Category = OptimizationCategory.Misc,
		};

		/// <summary>
		/// Builds each biome's wild animal list and commonality tables in one pass after defs load.
		/// </summary>
		/// <remarks>
		/// Vanilla scans every <see cref="PawnKindDef"/> on each <see cref="BiomeDef.AllWildAnimals"/>
		/// access and lazily builds the commonality dictionaries with <c>Dictionary.Add</c>, which
		/// throws and leaves the table half filled when a kind is listed twice.
		/// </remarks>
		/// <seealso cref="Helpers.BiomeAnimalIndex"/>
		/// <seealso cref="Patches.Early.Verse_BiomeDef_AllWildAnimals"/>
		public OptimizationOption OptBiomeAnimalIndex { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.BiomeAnimalIndex",
			Desc = "YaOpt.Setting.Option.BiomeAnimalIndex.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.BiomeAnimalIndex.Compatibility",
			SubCategory = "Startup",
			Category = OptimizationCategory.Misc,
		};

		/// <summary>
		/// Persists GiddyUp's session-only draw-offset cache across launches.
		/// </summary>
		/// <seealso cref="Helpers.GiddyUpOffsetCache"/>
		/// <seealso cref="Patches.Early.GiddyUp_Setup_ProcessPawnKinds"/>
		public OptimizationOption OptGiddyUpOffsetCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GiddyUpOffsetCache",
			Desc = "YaOpt.Setting.Option.GiddyUpOffsetCache.Desc",
			SubCategory = "Startup",
			Category = OptimizationCategory.Misc,
			FuncShow = (_) => YaOptGlobal.HasType("GiddyUp.Setup"),
		};

		/// <summary>
		/// Replaces Exosuit's full-document XML class rename with a value-only walk.
		/// </summary>
		/// <seealso cref="Helpers.ExosuitXmlBackCompatFast"/>
		/// <seealso cref="Patches.Early.Exosuit_XmlBackCompat_ApplyWorker"/>
		public OptimizationOption OptExosuitXmlFast { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ExosuitXmlFast",
			Desc = "YaOpt.Setting.Option.ExosuitXmlFast.Desc",
			SubCategory = "Startup",
			Category = OptimizationCategory.Misc,
			FuncShow = (_) => YaOptGlobal.HasType("Exosuit.XmlBackCompat"),
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
		/// Caches <see cref="StatDefOf.PsychicEntropyMax"/> for 20 ticks at the neural heat getters.
		/// </summary>
		/// <seealso cref="Patches.MultiTargets_PsychicEntropyStatCache"/>
		public OptimizationOption OptPsychicEntropyCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PsychicEntropyCache",
			Desc = "YaOpt.Setting.Option.PsychicEntropyCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.PsychicEntropyCache.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches <see cref="GenCelestial.CurCelestialSunGlow"/> for the remainder of the current tick.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_GenCelestial_CurCelestialSunGlow"/>
		public OptimizationOption OptCelestialSunGlowCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.CelestialSunGlowCache",
			Desc = "YaOpt.Setting.Option.CelestialSunGlowCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Memoises <see cref="RimWorld.Frame.WorkToBuild"/> by buildable def and stuff.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_Frame_WorkToBuild"/>
		public OptimizationOption OptFrameWorkToBuildCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.FrameWorkToBuildCache",
			Desc = "YaOpt.Setting.Option.FrameWorkToBuildCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Reuses the deterministic result of <see cref="ThingSetMaker_MarketValue"/>'s sampling for the
		/// same merged parameters and seed.
		/// </summary>
		/// <remarks>
		/// Reward and event item generation runs the same sample repeatedly: once per option to build the
		/// weighted option table and again to generate the chosen option. The sample is already wrapped
		/// in <c>Rand.PushState(seed)</c>/<c>PopState</c>, so identical parameters and seed always produce
		/// identical results and skipping the call does not advance the ambient random stream. Samples also
		/// read world state such as colonist titles and research progress, so entries expire every tick.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_ThingSetMaker_MarketValue_GeneratePossibleDefs"/>
		/// <seealso cref="Helpers.ThingSetMakerSampleCache"/>
		public OptimizationOption OptThingSetMakerSampleCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingSetMakerSampleCache",
			Desc = "YaOpt.Setting.Option.ThingSetMakerSampleCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.ThingSetMakerSampleCache.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Answers <see cref="ThingSetMaker_MarketValue"/>'s CanGenerate check from the first sampling
		/// iteration instead of running the full sample.
		/// </summary>
		/// <remarks>
		/// Option tables ask every option whether it can generate before picking one, so the full sample
		/// runs several times while only one option is actually generated. The first iteration already
		/// decides whether the result is non-empty. Automatically disabled when another mod patches the
		/// sampling members this check mirrors.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_ThingSetMaker_MarketValue_CanGenerateSub"/>
		/// <seealso cref="Helpers.ThingSetMakerCanGenerate"/>
		/// <seealso cref="Helpers.GenerationOptimizationCompat"/>
		public OptimizationOption OptThingSetMakerCanGenerateFastPath { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingSetMakerCanGenerateFastPath",
			Desc = "YaOpt.Setting.Option.ThingSetMakerCanGenerateFastPath.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.ThingSetMakerCanGenerateFastPath.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Memoises the min/max/single-value callbacks of
		/// <see cref="ThingSetMakerByTotalStatUtility.GenerateDefsWithPossibleTotalValue"/> for the
		/// duration of one call.
		/// </summary>
		/// <remarks>
		/// Vanilla evaluates the same stat up to three times per candidate (median threshold, minimum
		/// value and single-item value). Only callbacks declared in the vanilla assembly are wrapped; the
		/// cache is per call and discarded afterwards, so no values are shared between generations.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_ThingSetMakerByTotalStatUtility_GenerateDefsWithPossibleTotalValue"/>
		/// <seealso cref="Helpers.GenerationStatMemo"/>
		public OptimizationOption OptThingSetMakerStatCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ThingSetMakerStatCache",
			Desc = "YaOpt.Setting.Option.ThingSetMakerStatCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Caches pure def-derived results used while generating items: whether a weapon material is a
		/// derp choice, the allowed stuff list of each def, and the recipe used to price a def.
		/// </summary>
		/// <remarks>
		/// All three only depend on def data and are rebuilt when a save loads. Any foreign patch on the
		/// cached methods disables that part instead of bypassing it.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_PawnWeaponGenerator_IsDerpWeapon"/>
		/// <seealso cref="Patches.RimWorld_GenStuff_AllowedStuffsFor"/>
		/// <seealso cref="Patches.RimWorld_StatWorker_MarketValue_CalculableRecipe"/>
		/// <seealso cref="Helpers.GenerationDefCaches"/>
		public OptimizationOption OptGenerationDefCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.GenerationDefCache",
			Desc = "YaOpt.Setting.Option.GenerationDefCache.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Reuses values that stay constant for the duration of one apparel or weapon generation call.
		/// </summary>
		/// <remarks>
		/// Apparel generation retries the same candidate set up to 80 times and re-runs the same
		/// material restrictions and pawn temperature stats on every attempt. Weapon selection
		/// evaluates every candidate weight twice because the IList sampler makes two passes. The memo
		/// is opened and closed by the generation methods themselves and only skips pure lookups, so
		/// the random stream and the produced gear are unchanged.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_PawnWeaponGenerator_TryGenerateWeaponFor"/>
		/// <seealso cref="Patches.RimWorld_PawnWeaponGenerator_GetCommonality"/>
		/// <seealso cref="Patches.RimWorld_PawnApparelGenerator_GenerateStartingApparelFor"/>
		/// <seealso cref="Patches.RimWorld_PawnApparelGenerator_CanUseStuff"/>
		/// <seealso cref="Patches.Verse_GenTemperature_SafeTemperatureRange"/>
		/// <seealso cref="Helpers.PawnGearSessionCaches"/>
		public OptimizationOption OptPawnGearSessionCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PawnGearSessionCache",
			Desc = "YaOpt.Setting.Option.PawnGearSessionCache.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.PawnGearSessionCache.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Replaces apparel and weapon generation with constructive selection instead of retrying a full
		/// random candidate set.
		/// </summary>
		/// <remarks>
		/// Apparel generation rebuilds and re-validates the whole candidate set up to 80 times; the
		/// replacement filters once, seeds torso and leg coverage and fills the budget directly. Weapon
		/// selection evaluates every candidate twice; the replacement draws from a single weighted
		/// pass. Output is no longer identical to vanilla per seed, but mutual exclusivity, budget,
		/// required items, race restrictions and naked rules are preserved. Disabled by default. With
		/// Humanoid Alien Races loaded this neutralises its pool patches and filters per race instead;
		/// if any HAR gear patch cannot be found, or another mod patches the generation methods, the
		/// option disables itself and falls back to vanilla with HAR unchanged.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_PawnApparelGenerator_GenerateStartingApparelFor_Rebuild"/>
		/// <seealso cref="Patches.RimWorld_PawnWeaponGenerator_TryGenerateWeaponFor_Rebuild"/>
		/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_PawnGearNeutralizer"/>
		/// <seealso cref="Helpers.PawnGearRebuildCompat"/>
		/// <seealso cref="Helpers.PawnGearRebuild"/>
		/// <seealso cref="Helpers.PawnGearPools"/>
		public OptimizationOption OptPawnGearRebuild { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PawnGearRebuild",
			Desc = "YaOpt.Setting.Option.PawnGearRebuild.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.PawnGearRebuild.Compatibility",
			Category = OptimizationCategory.Tps,
			Default = false
		};

		/// <summary>
		/// Uses the destination map's live weather, pollution and vacuum state instead of world-tile
		/// season averages when the gear rewrite generates apparel.
		/// </summary>
		/// <remarks>
		/// The map is resolved once per Pawn from <see cref="Pawn.MapHeld"/> or the generation tile.
		/// All checks are constant-time field reads with no new allocations; when no map can be
		/// resolved the rewrite falls back to the exact vanilla calculations. Only effective while
		/// <see cref="OptPawnGearRebuild"/> is enabled.
		/// </remarks>
		/// <seealso cref="Helpers.PawnGearEnvironment"/>
		public OptimizationOption OptPawnGearEnvironment { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.PawnGearEnvironment",
			Desc = "YaOpt.Setting.Option.PawnGearEnvironment.Desc",
			Category = OptimizationCategory.Tps,
			Default = false,
			FuncShow = settings => settings.OptPawnGearRebuild.Enabled
		};

		/// <summary>
		/// Replaces the two-pass quest lodger lookup with one allocation-free scan.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_QuestUtility_IsQuestLodger"/>
		public OptimizationOption OptQuestLodgerFastPath { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.QuestLodgerFastPath",
			Desc = "YaOpt.Setting.Option.QuestLodgerFastPath.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Selects a natural random quest root by weighted random order with early CanRun exit.
		/// </summary>
		/// <remarks>
		/// Vanilla runs CanRun on every root, then samples the weighted valid set. This path first builds a
		/// weighted random order from the cheap natural weights, then runs CanRun in that order until the
		/// first valid root. Selection probability stays proportional to the natural weight, while roots
		/// after the first valid one are never tested. Disabled automatically when another mod owns the
		/// chooser.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_NaturalRandomQuestChooser_ChooseNaturalRandomQuest"/>
		/// <seealso cref="Helpers.QuestSelectionUtility"/>
		/// <seealso cref="Helpers.QuestGenerationOptimizationCompat"/>
		public OptimizationOption OptQuestSelectionFastPath { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.QuestSelectionFastPath",
			Desc = "YaOpt.Setting.Option.QuestSelectionFastPath.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.QuestSelectionFastPath.Compatibility",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Tries the Ancient Complex quest roots when normal selection returns no quest.
		/// </summary>
		/// <seealso cref="Helpers.QuestSelectionUtility.TryGetAncientComplexFallback"/>
		public OptimizationOption OptQuestAncientComplexFallback { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.QuestAncientComplexFallback",
			Desc = "YaOpt.Setting.Option.QuestAncientComplexFallback.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Replaces zero quest-generation points with the target's current threat points.
		/// </summary>
		/// <remarks>
		/// The fast selector may compute points to build weights while the caller still passes zero. The
		/// recent value is reused only for the same target and tick window, and explicit positive points
		/// are left untouched.
		/// </remarks>
		/// <seealso cref="Patches.RimWorld_QuestUtility_GenerateQuestAndMakeAvailable"/>
		/// <seealso cref="Helpers.QuestPointsUtility"/>
		public OptimizationOption OptQuestPointsNormalize { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.QuestPointsNormalize",
			Desc = "YaOpt.Setting.Option.QuestPointsNormalize.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Skips history event allocation when checking pawns without an ideo for teetotalism.
		/// </summary>
		/// <seealso cref="Patches.RimWorld_PawnUtility_IsTeetotaler"/>
		public OptimizationOption OptTeetotalerFastPath { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.TeetotalerFastPath",
			Desc = "YaOpt.Setting.Option.TeetotalerFastPath.Desc",
			Category = OptimizationCategory.Tps,
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptParallelPawnTick { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.ParallelPawnTick",
			Desc = "YaOpt.Setting.Option.ParallelPawnTick.Desc",
			Category = OptimizationCategory.Tps,
			Flags = OptimizationFlags.MultiplayerIncompatible,
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptLazyTextureLoad { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.LazyTextureLoad",
			Desc = "YaOpt.Setting.Option.LazyTextureLoad.Desc",
			NoteCompatibility = "YaOpt.Setting.Option.LazyTextureLoad.Compatibility",
			Category = OptimizationCategory.Misc,
		};

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
			SubCategory = "Startup",
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
			SubCategory = "Startup",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
		public OptimizationOption OptRuntimeInfoCache { get; } = new OptimizationOption
		{
			Name = "YaOpt.Setting.Option.RuntimeInfoCache",
			Desc = "YaOpt.Setting.Option.RuntimeInfoCache.Desc",
			Category = OptimizationCategory.Misc
		};

		/// <summary>
		/// Legacy setting retained for save compatibility; its 1.6 implementation has been removed.
		/// </summary>
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

		/// <summary>
		/// Options that a detected foreign patch already turned off by default. Saving the ids keeps
		/// the player's later decision (enabling them by hand) from being overridden on every load.
		/// </summary>
		private List<string> _conflictDefaultsApplied = new List<string>();

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
				ValidateOptions();
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
			Scribe_Collections.Look(ref _conflictDefaultsApplied, "conflictDefaultsApplied", LookMode.Value);
			if (_conflictDefaultsApplied == null)
				_conflictDefaultsApplied = new List<string>();
			if (YaOptGlobal.IsLibraryLoaded && Scribe.mode == LoadSaveMode.LoadingVars)
			{
				ValidateOptions();
			}
		}

		public void ValidateOptions()
		{
			foreach (var option in _allOptimizations)
			{
				option.Validate();
			}
		}

		/// <summary>
		/// Turns an option off as its default the first time a foreign patch is detected on it. A
		/// saved choice from an earlier session is respected, so a manual enable survives restarts.
		/// </summary>
		internal void ApplyPatchConflictDefault(string settingId)
		{
			if (_conflictDefaultsApplied == null)
				_conflictDefaultsApplied = new List<string>();
			if (_conflictDefaultsApplied.Contains(settingId))
				return;
			if (CompatibilityDefines.CachedBannedOptimizations.Contains(settingId))
				return;

			OptimizationOption option = null;
			for (var i = 0; i < _allOptimizations.Count; i++)
			{
				if (_allOptimizations[i].SettingId == settingId)
				{
					option = _allOptimizations[i];
					break;
				}
			}
			if (option == null)
				return;

			_conflictDefaultsApplied.Add(settingId);
			option.SetDefaultOff();
		}

		public void DoSettingsWindowContents(Rect inRect)
		{
			_settingsPanel.Draw(inRect);
		}
	}
}
