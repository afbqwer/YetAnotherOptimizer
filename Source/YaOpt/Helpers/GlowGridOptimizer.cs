using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Verse;
using Verse.Glow;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces the vanilla glow combine pass, which tests every light for every dirty cell, with a
	/// section-indexed one: a cell only visits the lights whose affected rect overlaps its 17x17
	/// section, and dirty cells are kept in a compact list instead of rescanning the whole map.
	/// </summary>
	/// <remarks>
	/// The per-light accumulation order is preserved (ascending light index inside a section), so the
	/// produced <see cref="Color32"/> values are byte-identical to vanilla. The flood fill
	/// (<c>ComputeGlowGridsJob</c>) is still the vanilla job; only the combine stage is replaced.
	/// </remarks>
	internal static class GlowGridOptimizer
	{
		private const int SectionSize = 17;

		/// <summary>Below this many dirty cells the combine runs inline; the job scheduler costs more than the work.</summary>
		private const int ParallelThreshold = 2048;

		private const int ComponentShift = 16;
		private const int ComponentMask = 0xFFFF;

		private static readonly ConditionalWeakTable<GlowGrid, State> States =
			new ConditionalWeakTable<GlowGrid, State>();

		// DirtyCell fires once per cell of an affected rect (up to 1681 times per light), so the
		// conditional-weak-table lookup must not run per call.
		[ThreadStatic] private static GlowGrid _cachedGrid;
		[ThreadStatic] private static State _cachedState;

		internal sealed class State
		{
			/// <summary>Per-cell flag used to deduplicate <see cref="DirtyCells"/>.</summary>
			public bool[] Dirty = Array.Empty<bool>();

			/// <summary>Packed (x | z &lt;&lt; 16) dirty cell coordinates.</summary>
			public readonly List<int> DirtyCells = new List<int>(4096);

			public int[] SectionStart = Array.Empty<int>();
			public int[] SectionLights = Array.Empty<int>();
			public int[] Cursor = Array.Empty<int>();
			public LightInfo[] Lights = Array.Empty<LightInfo>();

			public int SectionsX;
			public int SizeX;
			public int SizeZ;
			public int NumCells;
			public int IndexedLightCount;
			public bool HasCavePlants;
			public bool IndexValid;
		}

		/// <summary>Flattened per-light data used by the innermost loop.</summary>
		internal unsafe struct LightInfo
		{
			public int MinX;
			public int MinZ;
			public int Diameter;
			public float OverlightRadius;
			public bool IsCavePlant;
			public Color32* Colors;
		}

		private sealed class CombineContext
		{
			public LightInfo[] Lights;
			public int[] SectionStart;
			public int[] SectionLights;
			public int SectionsX;
			public int SizeX;
			public List<int> Cells;
			public bool[] DirtyFlags;
			public bool HasCavePlants;
			public NativeArray<Color32> Glow;
			public NativeArray<Color32> GlowNoCave;

			public void Execute(int k)
			{
				var packed = Cells[k];
				var x = packed & ComponentMask;
				var z = packed >> ComponentShift;
				ProcessCell(x, z);
			}

			internal unsafe void ProcessCell(int x, int z)
			{
				var index = x + z * SizeX;
				if (DirtyFlags != null)
				{
					DirtyFlags[index] = false;
				}

				var section = (z / SectionSize) * SectionsX + (x / SectionSize);
				var start = SectionStart[section];
				var end = SectionStart[section + 1];

				var sum = default(Color32);
				var lights = Lights;
				var sectionLights = SectionLights;

				if (HasCavePlants)
				{
					var sumNoCave = default(Color32);
					for (var t = start; t < end; t++)
					{
						var lightIndex = sectionLights[t];
						ref var light = ref lights[lightIndex];
						var dx = x - light.MinX;
						if ((uint)dx >= (uint)light.Diameter)
						{
							continue;
						}
						var dz = z - light.MinZ;
						if ((uint)dz >= (uint)light.Diameter)
						{
							continue;
						}

						var color = light.Colors[dx + dz * light.Diameter];
						AddColors(ref sum, in color, light.OverlightRadius);
						if (!light.IsCavePlant)
						{
							AddColors(ref sumNoCave, in color, light.OverlightRadius);
						}
					}

					GlowNoCave[index] = sumNoCave;
				}
				else
				{
					for (var t = start; t < end; t++)
					{
						var lightIndex = sectionLights[t];
						ref var light = ref lights[lightIndex];
						var dx = x - light.MinX;
						if ((uint)dx >= (uint)light.Diameter)
						{
							continue;
						}
						var dz = z - light.MinZ;
						if ((uint)dz >= (uint)light.Diameter)
						{
							continue;
						}

						var color = light.Colors[dx + dz * light.Diameter];
						AddColors(ref sum, in color, light.OverlightRadius);
					}

					// Without cave plants both sums are identical, so the second accumulation is skipped.
					GlowNoCave[index] = sum;
				}

				Glow[index] = sum;
			}
		}

		private struct CombineJob : IJobParallelFor
		{
			public CombineContext Context;

			public void Execute(int index)
			{
				Context.Execute(index);
			}
		}

		/// <summary>True when <paramref name="grid"/> exists and has live glow buffers.</summary>
		internal static bool IsReady(GlowGrid grid)
		{
			return grid != null && grid.accumulatedGlow.IsCreated &&
				   grid.accumulatedGlowNoCavePlants.IsCreated;
		}

		/// <summary>
		/// Drops all cached state. Called when the patches are (re)applied, because lights may have
		/// changed while the option was off and any cached index would then be stale.
		/// </summary>
		internal static void Reset()
		{
			States.Clear();
			_cachedGrid = null;
			_cachedState = null;
		}

		/// <summary>Records a cell as needing a combine.</summary>
		internal static void MarkDirty(GlowGrid grid, int x, int z)
		{
			var state = GetState(grid);
			var numCells = grid.indices.NumGridCells;
			if (state.Dirty.Length != numCells)
			{
				state.Dirty = new bool[numCells];
				state.DirtyCells.Clear();
				state.IndexValid = false;
			}

			var index = x + z * grid.indices.SizeX;
			if ((uint)index >= (uint)state.Dirty.Length || state.Dirty[index])
			{
				return;
			}

			state.Dirty[index] = true;
			state.DirtyCells.Add(Pack(x, z));
		}

		private static State GetState(GlowGrid grid)
		{
			if (ReferenceEquals(_cachedGrid, grid))
			{
				return _cachedState;
			}

			var state = States.GetOrCreateValue(grid);
			_cachedGrid = grid;
			_cachedState = state;
			return state;
		}

		/// <summary>
		/// Runs the vanilla flood fill when lights changed, then the indexed combine. Returns false
		/// only when there is nothing to do and the caller should fall through to vanilla.
		/// </summary>
		internal static bool Update(GlowGrid grid)
		{
			if (!IsReady(grid))
			{
				return false;
			}

			var state = GetState(grid);
			try
			{
				var lightsChanged = grid.anyDirtyLight;
				if (lightsChanged)
				{
					RunComputeGlowGridsJob(grid);
					grid.anyDirtyLight = false;
				}

				var allDirty = !grid.hasRunInitially;
				if (allDirty)
				{
					FillAllCells(grid, state);
				}

				if (state.DirtyCells.Count > 0)
				{
					if (lightsChanged || !state.IndexValid ||
						state.IndexedLightCount != grid.lights.Length ||
						state.NumCells != grid.indices.NumGridCells)
					{
						RebuildIndex(grid, state);
					}

					var context = new CombineContext
					{
						Lights = state.Lights,
						SectionStart = state.SectionStart,
						SectionLights = state.SectionLights,
						SectionsX = state.SectionsX,
						SizeX = state.SizeX,
						Cells = state.DirtyCells,
						DirtyFlags = allDirty ? null : state.Dirty,
						HasCavePlants = state.HasCavePlants,
						Glow = grid.accumulatedGlow,
						GlowNoCave = grid.accumulatedGlowNoCavePlants
					};

					var count = state.DirtyCells.Count;
					if (count < ParallelThreshold)
					{
						for (var i = 0; i < count; i++)
						{
							context.Execute(i);
						}
					}
					else
					{
						new CombineJob { Context = context }
							.Schedule(count, UnityData.GetIdealBatchCount(count)).Complete();
					}

					state.DirtyCells.Clear();
					grid.anyDirtyCell = false;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex.ToString());
				throw;
			}

			if (!grid.hasRunInitially)
			{
				grid.map.mapDrawer.WholeMapChanged(MapMeshFlagDefOf.GroundGlow);
			}
			grid.hasRunInitially = true;
			return true;
		}

		private static void RunComputeGlowGridsJob(GlowGrid grid)
		{
			var lights = grid.lights;
			new ComputeGlowGridsJob
			{
				lights = lights,
				indices = grid.indices,
				lightBlockers = grid.lightBlockers,
				statePool = GlowGrid.FloodFillerPool.pool,
				glowPool = grid.glowPool.pool,
				mapSize = grid.map.Size
			}.Schedule(lights.Length, UnityData.GetIdealBatchCount(lights.Length)).Complete();
		}

		private static void FillAllCells(GlowGrid grid, State state)
		{
			var sizeX = grid.indices.SizeX;
			var sizeZ = grid.indices.SizeZ;
			var cells = state.DirtyCells;
			cells.Clear();
			for (var z = 0; z < sizeZ; z++)
			{
				for (var x = 0; x < sizeX; x++)
				{
					cells.Add(Pack(x, z));
				}
			}
		}

		private static unsafe void RebuildIndex(GlowGrid grid, State state)
		{
			var sizeX = grid.indices.SizeX;
			var sizeZ = grid.indices.SizeZ;
			var sectionsX = (sizeX + SectionSize - 1) / SectionSize;
			var sectionsZ = (sizeZ + SectionSize - 1) / SectionSize;
			var sectionCount = sectionsX * sectionsZ;

			if (state.SectionStart.Length < sectionCount + 1)
			{
				state.SectionStart = new int[sectionCount + 1];
			}
			Array.Clear(state.SectionStart, 0, sectionCount + 1);

			var lights = grid.lights;
			var lightCount = lights.Length;
			if (state.Lights.Length < lightCount)
			{
				state.Lights = new LightInfo[Math.Max(lightCount, 16)];
			}

			var pool = grid.glowPool.pool;
			var hasCavePlants = false;

			for (var i = 0; i < lightCount; i++)
			{
				var light = lights[i];
				hasCavePlants |= light.isCavePlant;
				var minX = light.localGlowGridStartPos.x;
				var minZ = light.localGlowGridStartPos.z;
				var diameter = light.diameter;
				var maxX = minX + diameter - 1;
				var maxZ = minZ + diameter - 1;

				state.Lights[i] = new LightInfo
				{
					MinX = minX,
					MinZ = minZ,
					Diameter = diameter,
					OverlightRadius = light.overlightRadius,
					IsCavePlant = light.isCavePlant,
					Colors = pool[light.localGlowPoolIndex].colors.Ptr
				};

				if (maxX < 0 || maxZ < 0 || minX >= sizeX || minZ >= sizeZ)
				{
					continue;
				}

				var sx0 = Math.Max(0, minX) / SectionSize;
				var sx1 = Math.Min(sizeX - 1, maxX) / SectionSize;
				var sz0 = Math.Max(0, minZ) / SectionSize;
				var sz1 = Math.Min(sizeZ - 1, maxZ) / SectionSize;
				for (var sz = sz0; sz <= sz1; sz++)
				{
					var row = sz * sectionsX;
					for (var sx = sx0; sx <= sx1; sx++)
					{
						state.SectionStart[row + sx + 1]++;
					}
				}
			}

			for (var s = 0; s < sectionCount; s++)
			{
				state.SectionStart[s + 1] += state.SectionStart[s];
			}

			var total = state.SectionStart[sectionCount];
			if (state.SectionLights.Length < total)
			{
				state.SectionLights = new int[Math.Max(total, 64)];
			}
			if (state.Cursor.Length < sectionCount)
			{
				state.Cursor = new int[sectionCount];
			}
			Array.Copy(state.SectionStart, state.Cursor, sectionCount);

			for (var i = 0; i < lightCount; i++)
			{
				ref var info = ref state.Lights[i];
				if (info.Colors == null)
				{
					continue;
				}

				var maxX = info.MinX + info.Diameter - 1;
				var maxZ = info.MinZ + info.Diameter - 1;
				if (maxX < 0 || maxZ < 0 || info.MinX >= sizeX || info.MinZ >= sizeZ)
				{
					continue;
				}

				var sx0 = Math.Max(0, info.MinX) / SectionSize;
				var sx1 = Math.Min(sizeX - 1, maxX) / SectionSize;
				var sz0 = Math.Max(0, info.MinZ) / SectionSize;
				var sz1 = Math.Min(sizeZ - 1, maxZ) / SectionSize;
				for (var sz = sz0; sz <= sz1; sz++)
				{
					var row = sz * sectionsX;
					for (var sx = sx0; sx <= sx1; sx++)
					{
						state.SectionLights[state.Cursor[row + sx]++] = i;
					}
				}
			}

			state.SectionsX = sectionsX;
			state.SizeX = sizeX;
			state.SizeZ = sizeZ;
			state.NumCells = grid.indices.NumGridCells;
			state.IndexedLightCount = lightCount;
			state.HasCavePlants = hasCavePlants;
			state.IndexValid = true;
		}

		private static int Pack(int x, int z)
		{
			return (x & ComponentMask) | (z << ComponentShift);
		}

		/// <summary>Vanilla <c>CombineColorsJob.AddColors</c>, kept identical so light colors do not drift.</summary>
		private static void AddColors(ref Color32 existingSum, in Color32 toAdd, float overlightRadius)
		{
			var alpha = (int)toAdd.a;
			var colorInt = toAdd.AsColorInt();
			colorInt.ClampToNonNegative();
			colorInt.a = 0;
			if (colorInt.r <= 0 && colorInt.g <= 0 && colorInt.b <= 0)
			{
				return;
			}

			var sum = existingSum.AsColorInt();
			sum += colorInt;
			if (alpha < overlightRadius)
			{
				sum.a = 1;
			}
			sum.ProjectToColor32Fast(out var outColor);
			existingSum = outColor;
		}
	}
}
