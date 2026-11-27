using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Provides compatibility-aware pawn render-tree checks and parallel material preparation.
	/// </summary>
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

			if (type.IsMethodOverriden(nameof(PawnRenderNodeWorker.PreDraw)) ||
				type.IsMethodOverriden(nameof(PawnRenderNodeWorker.GetMaterialPropertyBlock)) ||
				type.IsMethodOverriden(nameof(PawnRenderNodeWorker.AppendDrawRequests)))
			{
				CustomMaterialPathWorkers.Add(type);
				YaOptMod.Debug($"Keeping {type.FullName} material updates on the main thread because it customizes the render request or material path.");
			}
		}

		public static bool CanParallelMaterialUpdate(PawnRenderNodeWorker worker)
		{
			return worker != null && !CustomMaterialPathWorkers.Contains(worker.GetType());
		}

		public static void PrepareMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
		{
			if (node == null)
				return;

			ref bool prepared = ref PrepatcherFields.MaterialPrepared(node);
			prepared = false;
			if (material == null || material.IsDeferredMaterial || !CanParallelMaterialUpdate(node.Worker))
				return;

			var block = node.MatPropBlock;
			if (block == null)
				return;

			if (parms.Statue)
				block.SetColor(ShaderPropertyIDs.Color, parms.statueColor.Value);
			else
				block.SetColor(ShaderPropertyIDs.Color, parms.tint * material.color);

			if (material.shader == ShaderDatabase.CutoutWithOverlay)
			{
				if (parms.pawn.Faction != null && material.GetMaskTexture() != null)
					PawnRenderUtility.SetMatPropBlockOverlay(block, parms.pawn.Faction.AllegianceColor);
				else
					PawnRenderUtility.SetMatPropBlockOverlay(block, Color.white, 0f);
			}

			prepared = true;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool IsMaterialPrepared(PawnRenderNode node)
		{
			return PrepatcherFields.MaterialPrepared(node);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void ClearMaterialPrepared(PawnRenderNode node)
		{
			PrepatcherFields.MaterialPrepared(node) = false;
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
