using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Provides a compatibility-aware fast check for dirty pawn render trees.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastRecacheRequested"/>
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

		static ParallelPreDrawHelper()
		{
			TypeSearcher.RegisterSearchingType(typeof(PawnRenderNode), PawnRenderNode_RecacheRequested_Checker);
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
