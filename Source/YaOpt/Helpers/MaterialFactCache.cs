using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Immutable material properties used by the parallel render preparation.
	/// </summary>
	internal sealed class MaterialFact
	{
		public readonly Color Color;
		public readonly bool Overlay;
		public readonly bool HasMask;

		public MaterialFact(Color color, bool overlay, bool hasMask)
		{
			Color = color;
			Overlay = overlay;
			HasMask = hasMask;
		}

		public bool SameAs(MaterialFact other)
		{
			return other != null && Color == other.Color && Overlay == other.Overlay &&
				HasMask == other.HasMask;
		}
	}

	/// <summary>
	/// Main-thread material fact cache consumed by render workers.
	/// </summary>
	/// <remarks>
	/// Workers only read immutable facts. The main thread fills and validates them, so Unity
	/// material accessors never move off the main thread. A material that changes while cached is
	/// removed and stays on the vanilla path for the rest of the session.
	/// </remarks>
	internal static class MaterialFactCache
	{
		private const int MaxFacts = 16384;
		private const int ValidatePerCall = 16;

		private sealed class MaterialReferenceComparer : IEqualityComparer<Material>
		{
			public bool Equals(Material x, Material y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(Material obj)
			{
				return RuntimeHelpers.GetHashCode(obj);
			}
		}

		private static readonly MaterialReferenceComparer Comparer = new MaterialReferenceComparer();

		private static readonly ConcurrentDictionary<Material, MaterialFact> Facts =
			new ConcurrentDictionary<Material, MaterialFact>(Comparer);

		private static readonly HashSet<Material> Unstable = new HashSet<Material>(Comparer);

		private static readonly List<Material> ValidationOrder = new List<Material>();

		private static int _validationIndex;

		static MaterialFactCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		public static bool TryGet(Material material, out MaterialFact fact)
		{
			return Facts.TryGetValue(material, out fact);
		}

		/// <summary>
		/// Fills or refreshes a fact after the main thread had to use the vanilla material path.
		/// </summary>
		public static void Observe(Material material)
		{
			if (material == null || Unstable.Contains(material))
				return;

			var fact = Read(material);
			if (Facts.TryGetValue(material, out var existing))
			{
				if (!existing.SameAs(fact))
					Facts[material] = fact;
				return;
			}

			if (ValidationOrder.Count >= MaxFacts)
				Clear();
			if (Facts.TryAdd(material, fact))
				ValidationOrder.Add(material);
		}

		/// <summary>
		/// Re-reads a bounded number of cached materials on the main thread.
		/// </summary>
		public static void ValidateSome()
		{
			var count = Math.Min(ValidatePerCall, ValidationOrder.Count);
			for (var checkedCount = 0; checkedCount < count && ValidationOrder.Count > 0; checkedCount++)
			{
				if (_validationIndex >= ValidationOrder.Count)
					_validationIndex = 0;

				var material = ValidationOrder[_validationIndex];
				if (ReferenceEquals(material, null))
				{
					RemoveAt(_validationIndex);
					continue;
				}

				if (material == null)
				{
					Facts.TryRemove(material, out _);
					RemoveAt(_validationIndex);
					continue;
				}

				var fact = Read(material);
				if (!Facts.TryGetValue(material, out var existing))
				{
					RemoveAt(_validationIndex);
					continue;
				}

				if (!existing.SameAs(fact))
				{
					MarkUnstableAt(_validationIndex);
					continue;
				}

				_validationIndex++;
			}
		}

		public static void Clear()
		{
			Facts.Clear();
			Unstable.Clear();
			ValidationOrder.Clear();
			_validationIndex = 0;
		}

		private static MaterialFact Read(Material material)
		{
			var overlay = material.shader == ShaderDatabase.CutoutWithOverlay;
			var hasMask = overlay && material.GetMaskTexture() != null;
			return new MaterialFact(material.color, overlay, hasMask);
		}

		private static void MarkUnstableAt(int index)
		{
			var material = ValidationOrder[index];
			Facts.TryRemove(material, out _);
			Unstable.Add(material);
			RemoveAt(index);
		}

		private static void RemoveAt(int index)
		{
			var last = ValidationOrder.Count - 1;
			ValidationOrder[index] = ValidationOrder[last];
			ValidationOrder.RemoveAt(last);
			if (_validationIndex >= ValidationOrder.Count)
				_validationIndex = 0;
		}
	}
}