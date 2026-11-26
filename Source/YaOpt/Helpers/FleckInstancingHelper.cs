using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Enables GPU instancing on fleck defs whose shader can consume per-instance data.
	/// </summary>
	/// <remarks>
	/// Vanilla only instances the defs that opt in through <c>renderInstanced</c>, but whether instancing
	/// works is a data property, not per-def authoring: <c>DrawBatch.Flush</c> submits an instanced
	/// batch through <c>Graphics.DrawMeshInstanced</c>, which binds a <c>_InstanceTransforms</c> storage
	/// buffer and - because flecks always carry a color - a <c>_Color</c> array. A shader can consume
	/// both only when it declares those storage slots; this runtime exposes them on
	/// <c>MaterialLayout.StorageBufferSlots</c>, parsed from the shader source and used by the engine
	/// itself when it binds the instance buffer. Defs whose shader does not declare both keep the
	/// per-object path, so only plain <see cref="Graphic_Fleck"/> qualifies: the per-fleck property-block
	/// classes (<see cref="Graphic_FleckPulse"/>, <see cref="Graphic_FleckSplash"/>) would still emit one
	/// batch per fleck no matter what the shader supports.
	/// </remarks>
	/// <seealso cref="Patches.Verse_FleckManager_Constructor"/>
	internal static class FleckInstancingHelper
	{
		private static readonly List<GraphicData> _enabled = new List<GraphicData>();

		private static bool _applied;

		private static bool _probeMismatchLogged;

		public static void Sync()
		{
			if (YaOptGlobal.Settings?.OptFleckAutoInstancing.Enabled != true)
			{
				Revert();
				return;
			}

			if (_applied)
				return;

			// Materializing a Graphic loads textures and shaders, so it is main-thread only.
			// Callers on the loading thread defer through LongEventHandler; never mark the
			// optimization applied from there.
			if (!UnityData.IsInMainThread)
				return;

			Apply();
		}

		private static void Apply()
		{
			Revert();
			try
			{
				var defs = DefDatabase<FleckDef>.AllDefsListForReading;
				// "proven" is a diagnostic, not a filter: every shader vanilla already instances is also
				// expected to declare the instance slots, so a proven shader that the probe rejects means the
				// probe is wrong for this build. It is never OR-ed into the decision either, because a mod can
				// mislabel a def and put an incapable shader into the proven set.
				var proven = new HashSet<Shader>();
				var capable = new HashSet<Shader>();
				var names = new StringBuilder();
				var count = 0;
				var mismatches = 0;
				foreach (var def in defs)
				{
					if (def.graphicData != null)
						count += Evaluate(def, def.graphicData, proven, capable, names, ref mismatches);
					if (def.randomGraphics == null)
						continue;
					foreach (var graphicData in def.randomGraphics)
						count += Evaluate(def, graphicData, proven, capable, names, ref mismatches);
				}

				if (proven.Count > 0 && capable.Count == 0)
				{
					// The layout probe answered "no" for every shader while vanilla instances some of them.
					// Our reading of the material layout does not hold for this build, so do not touch anything.
					Revert();
					if (!_probeMismatchLogged)
					{
						_probeMismatchLogged = true;
						YaOptMod.Error("FleckInstancing: the material-layout probe found no instance-capable shader " +
									   $"although vanilla instances {proven.Count} of them; skipping this optimization.");
					}
					return;
				}

				_applied = true;
				YaOptMod.Debug($"[FleckInstancing] enabled instancing on {count} fleck graphic(s)" +
							   (names.Length > 0 ? $": {names}" : string.Empty) +
							   (mismatches > 0 ? $"; {mismatches} def(s) marked instanced without instance slots" : string.Empty));
			}
			catch (Exception ex)
			{
				YaOptMod.Error("FleckInstancing: failed to apply, staying on vanilla instancing flags.\n" + ex);
			}
		}

		private static void Revert()
		{
			if (_enabled.Count == 0)
				return;

			foreach (var graphicData in _enabled)
			{
				if (graphicData != null)
					graphicData.renderInstanced = false;
			}

			_enabled.Clear();
			_applied = false;
		}

		private static int Evaluate(FleckDef def, GraphicData graphicData, HashSet<Shader> proven,
			HashSet<Shader> capable, StringBuilder names, ref int mismatches)
		{
			if (graphicData == null)
				return 0;

			var material = GetMaterial(graphicData);
			if (material == null)
				return 0;

			var plain = graphicData.graphicClass == typeof(Graphic_Fleck);
			var canInstance = ShaderSupportsInstancing(material);
			if (canInstance)
				capable.Add(material.shader);

			if (graphicData.renderInstanced && plain)
			{
				proven.Add(material.shader);
				if (!canInstance)
					mismatches++;
			}

			if (graphicData.renderInstanced || !plain || !canInstance)
				return 0;

			graphicData.renderInstanced = true;
			_enabled.Add(graphicData);
			if (names.Length < 400)
				names.Append(names.Length == 0 ? string.Empty : ", ").Append(def.defName);
			return 1;
		}

		/// <summary>
		/// True when the shader declares the two storage buffers an instanced fleck batch binds.
		/// </summary>
		private static bool ShaderSupportsInstancing(Material material)
		{
			// Material.enableInstancing is a plain auto-property in this runtime and reports nothing
			// about shader support; the material layout is what the engine binds the buffers against.
			var layout = material.engineMaterial?.Shader?.MaterialLayout;
			if (layout == null || layout.HasErrors)
				return false;

			var transforms = false;
			var colors = false;
			foreach (var slot in layout.StorageBufferSlots)
			{
				if (slot.PropertyName == "_InstanceTransforms")
					transforms = true;
				else if (slot.PropertyName == "_Color")
					colors = true;
			}
			return transforms && colors;
		}

		private static Material GetMaterial(GraphicData graphicData)
		{
			try
			{
				var graphic = graphicData.Graphic;
				return graphic?.MatSingle;
			}
			catch (Exception ex)
			{
				YaOptMod.Warning($"FleckInstancing: could not initialize graphic for {graphicData.texPath}: {ex.Message}");
				return null;
			}
		}
	}
}