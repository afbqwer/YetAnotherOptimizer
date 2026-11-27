using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Provides compatibility-aware pawn render-tree checks and parallel material preparation.
	/// </summary>
	/// <remarks>
	/// Material preparation writes the node's own <see cref="PawnRenderNode.MatPropBlock"/>, which
	/// matches the vanilla one-request-per-node render path. See
	/// <see cref="YaOptSettings.OptParallelMaterialUpdate"/> for the documented limitations.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastRecacheRequested"/>
	/// <seealso cref="YaOptSettings.OptParallelMaterialUpdate"/>
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

		static ParallelPreDrawHelper()
		{
			TypeSearcher.RegisterSearchingType(typeof(PawnRenderNode), PawnRenderNode_RecacheRequested_Checker);
			TypeSearcher.RegisterSearchingType(typeof(PawnRenderNodeWorker), PawnRenderNodeWorker_MaterialPath_Checker);
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
		/// Returns whether <see cref="PrepareMaterialPropertyBlock"/> can fill the block for this draw.
		/// </summary>
		/// <remarks>
		/// The draw patches use the same check before returning the pre-filled block. This keeps
		/// deferred materials and custom worker paths on the vanilla main-thread path.
		/// </remarks>
		public static bool CanUsePreparedBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			return node != null && material != null && !material.IsDeferredMaterial &&
				(!parms.Statue || parms.statueColor.HasValue) &&
				CanParallelMaterialUpdate(node.Worker);
		}

		public static void PrepareMaterialPropertyBlocks(List<PawnGraphicDrawRequest> requests, PawnDrawParms parms)
		{
			for (var i = 0; i < requests.Count; i++)
			{
				var request = requests[i];
				PrepareMaterialPropertyBlock(request.node, request.material, parms);
			}
		}

		private static void PrepareMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			if (!CanUsePreparedBlock(node, material, parms))
				return;

			var block = node.MatPropBlock;
			if (parms.Statue)
			{
				block.SetColor(ShaderPropertyIDs.Color, parms.statueColor.Value);
			}
			else
			{
				block.SetColor(ShaderPropertyIDs.Color, parms.tint * material.color);
			}

			if (material.shader == ShaderDatabase.CutoutWithOverlay)
			{
				if (parms.pawn.Faction != null && material.GetMaskTexture() != null)
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
