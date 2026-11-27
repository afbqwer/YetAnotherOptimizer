using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Provides compatibility-aware pawn render-tree checks, early render preparation and parallel
	/// material preparation.
	/// </summary>
	/// <remarks>
	/// Material preparation writes the node's own <see cref="PawnRenderNode.MatPropBlock"/>, which
	/// matches the vanilla one-request-per-node render path. See
	/// <see cref="YaOptSettings.OptParallelMaterialUpdate"/> for the documented limitations.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastRecacheRequested"/>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
	/// <seealso cref="YaOptSettings.OptEarlyRenderPrepare"/>
	[StaticConstructorOnStartup]
	internal static class ParallelPreDrawHelper
	{
		/// <summary>
		/// Indicates whether the fast <see cref="FastRecacheRequested"/> method can be used.
		/// </summary>
		/// <remarks>
		/// Disabled if any mod overrides <see cref="PawnRenderNode.RecacheRequested"/>,
		/// as the fast path only works with the vanilla implementation.
		/// </remarks>
		public static bool FastRecacheRequestedAvailable { get; private set; } = true;

		private static readonly HashSet<Type> CustomMaterialPathWorkers = new HashSet<Type>();

		private static volatile int _renderFrame;

		static ParallelPreDrawHelper()
		{
			TypeSearcher.RegisterSearchingType(typeof(PawnRenderNode), PawnRenderNode_RecacheRequested_Checker);
			TypeSearcher.RegisterSearchingType(typeof(PawnRenderNodeWorker), PawnRenderNodeWorker_MaterialPath_Checker);
			UpdateCallbackHelper.RegisterClearCacheCallback(AbortEarlyRenderPrepare);
		}

		/// <summary>
		/// Indicates whether the early render preparation transpilers are installed.
		/// </summary>
		public static bool EarlyRenderPrepareActive { get; set; }

		private static NativeArray<DynamicDrawManager.ThingCullDetails> _earlyDetails;

		private static Map _earlyDetailsMap;

		private static JobHandle _cullJobHandle;

		private static JobHandle _preDrawJobHandle;

		private static bool _hasEarlyDetails;

		private static bool _earlyPreDrawPrepared;

		private static bool _awaitingEarlyCull;

		private static bool _awaitingEarlyPreDraw;

		private static bool _cullJobPending;

		private static bool _preDrawJobPending;

		/// <summary>
		/// Starts culling dynamic drawables before the map mesh is updated and drawn.
		/// </summary>
		/// <remarks>
		/// The allocated details array is handed to <see cref="DynamicDrawManager.DrawDynamicThings"/>
		/// later in the same frame. If the draw phase is skipped, the next start (or a game load)
		/// disposes the leftover array.
		/// </remarks>
		public static void StartCullingJob(Map map)
		{
			if (!EarlyRenderPrepareActive)
				return;

			AbortEarlyRenderPrepare();

			if (map == null || map.Disposed || !DebugViewSettings.drawThingsDynamic)
				return;

			var manager = map.dynamicDrawManager;
			var count = manager.drawThings.Count;
			if (count == 0)
				return;

			_earlyDetails = new NativeArray<DynamicDrawManager.ThingCullDetails>(
				count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
			_earlyDetailsMap = map;
			_hasEarlyDetails = true;
			_earlyPreDrawPrepared = false;

			try
			{
				_awaitingEarlyCull = true;
				manager.ComputeCulledThings(_earlyDetails);
			}
			catch (Exception ex)
			{
				YaOptMod.Error($"Early render preparation failed while starting the cull job: {ex}");
				AbortEarlyRenderPrepare();
			}
			finally
			{
				_awaitingEarlyCull = false;
			}
		}

		/// <summary>
		/// Runs the main-thread part of dynamic render preparation and starts the parallel pre-draw job
		/// before the map mesh is drawn.
		/// </summary>
		public static void StartPreDrawJob(Map map)
		{
			if (!EarlyRenderPrepareActive || !_hasEarlyDetails)
				return;

			if (map == null || map.Disposed || _earlyDetailsMap != map || !DebugViewSettings.drawThingsDynamic)
			{
				AbortEarlyRenderPrepare();
				return;
			}

			WaitUntilCullJobComplete();

			var manager = map.dynamicDrawManager;
			if (manager.drawThings.Count != _earlyDetails.Length)
			{
				RebuildEarlyDetails(manager);
				if (!_hasEarlyDetails)
					return;
				WaitUntilCullJobComplete();
			}

			if (!DebugViewSettings.singleThreadedDrawing)
			{
				if (YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled)
					PrepareMaterialState();

				var details = _earlyDetails;
				var drawThings = manager.drawThings;
				for (var i = 0; i < details.Length; i++)
				{
					if (details[i].shouldDraw)
						drawThings[i].DynamicDrawPhase(DrawPhase.EnsureInitialized);
				}

				try
				{
					_awaitingEarlyPreDraw = true;
					manager.PreDrawVisibleThings(details);
				}
				catch (Exception ex)
				{
					YaOptMod.Error($"Early render preparation failed while starting the pre-draw job: {ex}");
					AbortEarlyRenderPrepare();
					return;
				}
				finally
				{
					_awaitingEarlyPreDraw = false;
				}
			}

			_earlyPreDrawPrepared = true;
		}

		/// <summary>
		/// Returns the early-prepared cull details, preparing them synchronously when the early start
		/// did not run (for example when another mod calls <see cref="DynamicDrawManager.DrawDynamicThings"/>
		/// outside <see cref="Verse.Map.MapUpdate"/>).
		/// </summary>
		public static NativeArray<DynamicDrawManager.ThingCullDetails> TakeOrPrepareDetails(DynamicDrawManager manager)
		{
			if (_hasEarlyDetails && _earlyDetailsMap == manager.map && _earlyPreDrawPrepared)
			{
				WaitUntilCullJobComplete();
				WaitUntilPreDrawJobComplete();

				if (manager.drawThings.Count == _earlyDetails.Length)
				{
					var details = _earlyDetails;
					_earlyDetails = default;
					_earlyDetailsMap = null;
					_hasEarlyDetails = false;
					_earlyPreDrawPrepared = false;
					return details;
				}
			}

			AbortEarlyRenderPrepare();
			return PrepareDetailsNow(manager);
		}

		/// <summary>
		/// Stores a deferred cull job handle; called from the transpiled
		/// <see cref="DynamicDrawManager.ComputeCulledThings"/>.
		/// </summary>
		public static void CompleteCullingJob(ref JobHandle handle)
		{
			if (EarlyRenderPrepareActive && _awaitingEarlyCull)
			{
				_awaitingEarlyCull = false;
				_cullJobHandle = handle;
				_cullJobPending = true;
				return;
			}

			handle.Complete();
		}

		/// <summary>
		/// Stores a deferred pre-draw job handle; called from the transpiled
		/// <see cref="DynamicDrawManager.PreDrawVisibleThings"/>.
		/// </summary>
		public static void CompletePreDrawJob(ref JobHandle handle)
		{
			if (EarlyRenderPrepareActive && _awaitingEarlyPreDraw)
			{
				_awaitingEarlyPreDraw = false;
				_preDrawJobHandle = handle;
				_preDrawJobPending = true;
				return;
			}

			handle.Complete();
		}

		/// <summary>
		/// Completes any outstanding early render preparation work and releases its details array.
		/// </summary>
		public static void AbortEarlyRenderPrepare()
		{
			try
			{
				WaitUntilCullJobComplete();
			}
			catch (Exception ex)
			{
				YaOptMod.Error($"Early render preparation cull job failed: {ex}");
			}

			try
			{
				WaitUntilPreDrawJobComplete();
			}
			catch (Exception ex)
			{
				YaOptMod.Error($"Early render preparation pre-draw job failed: {ex}");
			}

			DisposeEarlyDetails();

			_earlyDetailsMap = null;
			_earlyPreDrawPrepared = false;
			_awaitingEarlyCull = false;
			_awaitingEarlyPreDraw = false;
		}

		private static void RebuildEarlyDetails(DynamicDrawManager manager)
		{
			WaitUntilCullJobComplete();
			DisposeEarlyDetails();

			_earlyDetailsMap = manager.map;
			_earlyPreDrawPrepared = false;

			var count = manager.drawThings.Count;
			if (count == 0)
				return;

			_earlyDetails = new NativeArray<DynamicDrawManager.ThingCullDetails>(
				count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
			_hasEarlyDetails = true;

			try
			{
				_awaitingEarlyCull = true;
				manager.ComputeCulledThings(_earlyDetails);
			}
			catch (Exception ex)
			{
				YaOptMod.Error($"Early render preparation failed while rebuilding the cull details: {ex}");
				AbortEarlyRenderPrepare();
			}
			finally
			{
				_awaitingEarlyCull = false;
			}
		}

		private static NativeArray<DynamicDrawManager.ThingCullDetails> PrepareDetailsNow(DynamicDrawManager manager)
		{
			var details = new NativeArray<DynamicDrawManager.ThingCullDetails>(
				manager.drawThings.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);

			_awaitingEarlyCull = false;
			_awaitingEarlyPreDraw = false;

			manager.ComputeCulledThings(details);

			if (!DebugViewSettings.singleThreadedDrawing)
			{
				if (YaOptGlobal.Settings.OptParallelMaterialUpdate.Enabled)
					PrepareMaterialState();

				var drawThings = manager.drawThings;
				for (var i = 0; i < details.Length; i++)
				{
					if (details[i].shouldDraw)
						drawThings[i].DynamicDrawPhase(DrawPhase.EnsureInitialized);
				}

				manager.PreDrawVisibleThings(details);
			}

			return details;
		}

		private static void WaitUntilCullJobComplete()
		{
			if (!_cullJobPending)
				return;

			_cullJobPending = false;
			_cullJobHandle.Complete();
		}

		private static void WaitUntilPreDrawJobComplete()
		{
			if (!_preDrawJobPending)
				return;

			_preDrawJobPending = false;
			_preDrawJobHandle.Complete();
		}

		private static void DisposeEarlyDetails()
		{
			if (!_hasEarlyDetails)
				return;

			_hasEarlyDetails = false;
			if (_earlyDetails.IsCreated)
			{
				try
				{
					_earlyDetails.Dispose();
				}
				catch (Exception ex)
				{
					YaOptMod.Error($"Early render preparation details could not be disposed: {ex}");
				}
			}

			_earlyDetails = default;
		}

		private static void PawnRenderNode_RecacheRequested_Checker(Type type)
		{
			if (type == typeof(PawnRenderNode))
				return;

			if (type.GetProperty(nameof(PawnRenderNode.RecacheRequested),
					BindingFlags.Instance | BindingFlags.Public)?.GetMethod?.IsOverriden() == true)
			{
				YaOptMod.Warning($"Found a derived PawnRenderNode class ({type.FullName}) with overriding RecacheRequested. " +
								 "FastRecacheRequested optimization is disabled now. " +
								 "This is not an error. Just a compatibility measure.");
				FastRecacheRequestedAvailable = false;
			}
		}

		private static void PawnRenderNodeWorker_MaterialPath_Checker(Type type)
		{
			if (type == typeof(PawnRenderNodeWorker))
				return;

			if (IsMethodOverridden(type, nameof(PawnRenderNodeWorker.PreDraw)) ||
				IsMethodOverridden(type, nameof(PawnRenderNodeWorker.GetMaterialPropertyBlock)) ||
				IsMethodOverridden(type, "GetGraphic") ||
				IsMethodOverridden(type, "GetGraphicState") ||
				IsMethodOverridden(type, "GetMaterial") ||
				IsMethodOverridden(type, nameof(PawnRenderNodeWorker.AppendDrawRequests)) ||
				IsMethodOverridden(type, nameof(PawnRenderNodeWorker.GetFinalizedMaterial)))
			{
				CustomMaterialPathWorkers.Add(type);
				YaOptMod.Debug($"Keeping {type.FullName} material updates on the main thread because it customizes the render request or material path.");
			}
		}

		private static bool IsMethodOverridden(Type type, string name)
		{
			return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?.IsOverriden() == true;
		}

		public static bool CanParallelMaterialUpdate(PawnRenderNodeWorker worker)
		{
			return worker != null && !CustomMaterialPathWorkers.Contains(worker.GetType());
		}

		/// <summary>
		/// Initializes lazy faction colors and validates cached material facts before workers run.
		/// </summary>
		public static void PrepareMaterialState()
		{
			_renderFrame = Time.frameCount;
			MaterialFactCache.ValidateSome();
			PrepareFactionColors();
		}

		/// <summary>
		/// Initializes lazy faction colors before material preparation runs on worker threads.
		/// </summary>
		public static void PrepareFactionColors()
		{
			var factions = Find.FactionManager.AllFactionsListForReading;
			for (var i = 0; i < factions.Count; i++)
			{
				_ = factions[i].AllegianceColor;
			}
		}

		/// <summary>
		/// Returns whether the draw phase may use the block prepared for the current request.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool CanUsePreparedBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			return IsEligible(node, material, parms) && PrepatcherState.PreparedMaterialActive &&
				node.PreparedMaterial().Matches(Time.frameCount, material, parms);
		}

		/// <summary>
		/// Consumes the prepared stamp so another request or draw cannot reuse the cleared block.
		/// </summary>
		public static bool TryUsePreparedBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			if (!CanUsePreparedBlock(node, material, parms))
			{
				MaterialFactCache.Observe(material);
				return false;
			}

			node.PreparedMaterial().Clear();
			return true;
		}

		private static bool IsEligible(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			return node != null && material != null && !material.IsDeferredMaterial &&
				!node.CheckMaterialEveryDrawRequest &&
				(!parms.Statue || parms.statueColor.HasValue) &&
				CanParallelMaterialUpdate(node.Worker);
		}

		public static void PrepareMaterialPropertyBlocks(List<PawnGraphicDrawRequest> requests, PawnDrawParms parms)
		{
			if (!PrepatcherState.PreparedMaterialActive)
				return;

			var frame = _renderFrame;
			for (var i = 0; i < requests.Count; i++)
			{
				var request = requests[i];
				var node = request.node;
				var material = request.material;
				if (!IsEligible(node, material, parms) ||
					!MaterialFactCache.TryGet(material, out var fact))
					continue;

				WriteMaterialPropertyBlock(node, parms, fact);
				node.PreparedMaterial().Set(frame, material, parms);
			}
		}

		private static void WriteMaterialPropertyBlock(PawnRenderNode node, PawnDrawParms parms,
			MaterialFact fact)
		{
			var block = node.MatPropBlock;
			if (parms.Statue)
			{
				block.SetColor(ShaderPropertyIDs.Color, parms.statueColor.Value);
			}
			else
			{
				block.SetColor(ShaderPropertyIDs.Color, parms.tint * fact.Color);
			}

			if (fact.Overlay)
			{
				if (parms.pawn.Faction != null && fact.HasMask)
					PawnRenderUtility.SetMatPropBlockOverlay(block, parms.pawn.Faction.AllegianceColor);
				else
					PawnRenderUtility.SetMatPropBlockOverlay(block, Color.white, 0f);
			}
		}

		/// <summary>
		/// Fast alternative to <see cref="PawnRenderNode.RecacheRequested"/> that inlines child node checks.
		/// </summary>
		/// <remarks>
		/// Inlines the first three levels, which cover almost all render trees, and falls back to
		/// the virtual property for deeper nodes. Modded overrides use the vanilla property throughout.
		/// </remarks>
		public static bool FastRecacheRequested(PawnRenderNode node)
		{
			if (!FastRecacheRequestedAvailable)
				return node.RecacheRequested;

			if (node.requestRecache)
				return true;

			if (node.children == null)
				return false;

			foreach (var child in node.children)
			{
				if (child.requestRecache)
					return true;

				if (child.children == null)
					continue;

				foreach (var grandChild in child.children)
				{
					if (grandChild.requestRecache)
						return true;

					if (grandChild.children == null)
						continue;

					foreach (var greatGrandChild in grandChild.children)
					{
						if (greatGrandChild.RecacheRequested)
							return true;
					}
				}
			}

			return false;
		}
	}
}
