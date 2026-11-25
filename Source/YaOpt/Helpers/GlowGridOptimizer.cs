using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LudeonTK;
using RimWorld;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

		/// <summary>
		/// Frames between unconditional index invalidations. Covers a mod that rewrites
		/// <c>lights</c> directly without going through the registration methods we hook; the index is
		/// rebuilt on the next combine or <c>DirtyLightsAround</c> call.
		/// </summary>
		private const int SanityRebuildFrames = 300;

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
			public int FramesSinceRebuild;
			public bool HasCavePlants;
			public bool IndexValid;

			/// <summary>Per-light scratch used by the prefill job.</summary>
			public bool[] LeftDirty = Array.Empty<bool>();
			public ulong[] Signatures = Array.Empty<ulong>();

			/// <summary>Last computed local colors per light id, keyed for re-registration reuse.</summary>
			public readonly ConcurrentDictionary<int, CachedGlow> FillCache =
				new ConcurrentDictionary<int, CachedGlow>();
		}

		/// <summary>
		/// Cached result of the flood fill for one light. Reused when a light is re-registered with
		/// identical geometry, color and blocker pattern, which is what happens on every power/flick
		/// toggle because <c>CompGlower.UpdateLit</c> deregisters and re-registers.
		/// </summary>
		internal sealed class CachedGlow
		{
			public const int MaxEntries = 1024;

			public int Diameter;
			public float Radius;
			public float Overlight;
			public ColorInt Color;
			public ulong Signature;
			public Color32[] Colors;

			public bool Matches(in GlowLight light, ulong signature)
			{
				return Diameter == light.diameter && Radius == light.glowRadius &&
					   Overlight == light.overlightRadius && Color == light.glowColor &&
					   Signature == signature && Colors.Length == Diameter * Diameter;
			}
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

		/// <summary>Called from the light registration/unregistration postfixes; the index must be rebuilt.</summary>
		internal static void NotifyLightsChanged(GlowGrid grid)
		{
			GetState(grid).IndexValid = false;
		}

		/// <summary>
		/// Section-indexed replacement for <c>GlowGrid.DirtyLightsAround</c>: only lights whose rect
		/// overlaps the cell's section are considered instead of every light on the map.
		/// </summary>
		/// <returns>True when the call was handled.</returns>
		internal static bool DirtyLightsAround(GlowGrid grid, IntVec3 cell)
		{
			if (!grid.hasRunInitially || !IsReady(grid))
			{
				return false;
			}

			try
			{
				var state = GetState(grid);
				if (!state.IndexValid ||
					state.IndexedLightCount != grid.lights.Length ||
					state.NumCells != grid.indices.NumGridCells)
				{
					RebuildIndex(grid, state);
				}

				if ((uint)cell.x >= (uint)state.SizeX || (uint)cell.z >= (uint)state.SizeZ)
				{
					return false;
				}

				var section = (cell.z / SectionSize) * state.SectionsX + (cell.x / SectionSize);
				var start = state.SectionStart[section];
				var end = state.SectionStart[section + 1];
				var lights = grid.lights;
				for (var t = start; t < end; t++)
				{
					var index = state.SectionLights[t];
					if ((uint)index >= (uint)lights.Length)
					{
						continue;
					}
					var light = lights[index];
					if (light.dirty)
					{
						continue;
					}

					var rect = light.AffectedRect;
					if (!rect.Contains(cell))
					{
						continue;
					}

					light.dirty = true;
					lights[index] = light;
					foreach (var c in rect)
					{
						if (c.InBounds(grid.map) && grid.indices.Contains(c))
						{
							grid.DirtyCell(c);
						}
					}
					grid.anyDirtyLight = true;
				}

				return true;
			}
			catch (Exception ex)
			{
				Log.Error(ex.ToString());
				return false;
			}
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
				state.FramesSinceRebuild++;
				if (state.FramesSinceRebuild >= SanityRebuildFrames)
				{
					state.FramesSinceRebuild = 0;
					state.IndexValid = false;
				}

				// A foreign or debug path may set the vanilla dirty bitmap without going through our
				// DirtyCell postfix. Let vanilla consume it when we have no tracked cells to combine.
				if (grid.hasRunInitially && grid.anyDirtyCell && state.DirtyCells.Count == 0)
				{
					return false;
				}

				var lightsChanged = grid.anyDirtyLight;
				if (lightsChanged)
				{
					RunComputeGlowGridsJob(grid, state);
					grid.anyDirtyLight = false;
				}

				var allDirty = !grid.hasRunInitially;
				if (allDirty)
				{
					FillAllCells(grid, state);
				}

				if (state.DirtyCells.Count > 0)
				{
					var rebuild = lightsChanged || !state.IndexValid ||
								  state.IndexedLightCount != grid.lights.Length ||
								  state.NumCells != grid.indices.NumGridCells;
					if (rebuild)
					{
						state.FramesSinceRebuild = 0;
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

		private static void RunComputeGlowGridsJob(GlowGrid grid, State state)
		{
			var lights = grid.lights;
			var count = lights.Length;
			if (count == 0)
			{
				return;
			}

			if (state.LeftDirty.Length < count)
			{
				state.LeftDirty = new bool[count];
			}
			if (state.Signatures.Length < count)
			{
				state.Signatures = new ulong[count];
			}
			Array.Clear(state.LeftDirty, 0, count);

			var batch = UnityData.GetIdealBatchCount(count);
			var prefill = new PrefillJob
			{
				Lights = lights,
				Blockers = grid.lightBlockers,
				Indices = grid.indices,
				Pool = grid.glowPool.pool,
				SizeX = grid.indices.SizeX,
				SizeZ = grid.indices.SizeZ,
				MapSize = grid.map.Size,
				LeftDirty = state.LeftDirty,
				Signatures = state.Signatures,
				Cache = state.FillCache
			}.Schedule(count, batch);

			new ComputeGlowGridsJob
			{
				lights = lights,
				indices = grid.indices,
				lightBlockers = grid.lightBlockers,
				statePool = GlowGrid.FloodFillerPool.pool,
				glowPool = grid.glowPool.pool,
				mapSize = grid.map.Size
			}.Schedule(count, batch, prefill).Complete();

			StoreCache(grid, state, count);
		}

		/// <summary>Copies the freshly computed local colors of every light the job handled into the cache.</summary>
		private static void StoreCache(GlowGrid grid, State state, int count)
		{
			var left = state.LeftDirty;
			var lights = grid.lights;
			var pool = grid.glowPool.pool;
			var cache = state.FillCache;
			for (var i = 0; i < count; i++)
			{
				if (!left[i])
				{
					continue;
				}
				left[i] = false;

				var light = lights[i];
				var cells = light.diameter * light.diameter;
				unsafe
				{
					var colors = pool[light.localGlowPoolIndex].colors;
					if (colors.Ptr == null || colors.Length < cells)
					{
						continue;
					}

					if (cache.Count >= CachedGlow.MaxEntries)
					{
						cache.Clear();
					}

					var copy = new Color32[cells];
					var src = colors.Ptr;
					for (var k = 0; k < cells; k++)
					{
						copy[k] = src[k];
					}
					cache[light.id] = new CachedGlow
					{
						Diameter = light.diameter,
						Radius = light.glowRadius,
						Overlight = light.overlightRadius,
						Color = light.glowColor,
						Signature = state.Signatures[i],
						Colors = copy
					};
				}
			}
		}

		/// <summary>
		/// Fills unobstructed lights with the exact octile distance field and restores lights whose
		/// blocker pattern and color are unchanged from the cache, so the vanilla flood fill only runs
		/// for genuinely new geometry.
		/// </summary>
		private struct PrefillJob : IJobParallelFor
		{
			public NativeList<GlowLight> Lights;
			public NativeBitArray Blockers;
			public CellIndices Indices;
			public NativeList<LocalGlowArea> Pool;
			public int SizeX;
			public int SizeZ;
			public IntVec3 MapSize;
			public bool[] LeftDirty;
			public ulong[] Signatures;
			public ConcurrentDictionary<int, CachedGlow> Cache;

			public void Execute(int index)
			{
				var light = Lights[index];
				if (!light.dirty)
				{
					return;
				}

				var minX = light.localGlowGridStartPos.x;
				var minZ = light.localGlowGridStartPos.z;
				var x0 = Math.Max(0, minX);
				var z0 = Math.Max(0, minZ);
				var x1 = Math.Min(SizeX - 1, minX + light.diameter - 1);
				var z1 = Math.Min(SizeZ - 1, minZ + light.diameter - 1);

				var signature = 14695981039346656037UL;
				signature ^= (ulong)(uint)light.position.x;
				signature *= 1099511628211UL;
				signature ^= (ulong)(uint)light.position.z;
				signature *= 1099511628211UL;
				var blocked = false;
				for (var z = z0; z <= z1; z++)
				{
					var row = z * SizeX;
					for (var x = x0; x <= x1; x++)
					{
						var cellIndex = row + x;
						if (Blockers.IsSet(cellIndex))
						{
							blocked = true;
							signature ^= (ulong)cellIndex;
							signature *= 1099511628211UL;
						}
					}
				}
				Signatures[index] = signature;

				var positionInMap = (uint)light.position.x < (uint)MapSize.x &&
									(uint)light.position.z < (uint)MapSize.z;
				if (!blocked && positionInMap)
				{
					FillRadial(ref light, Pool[light.localGlowPoolIndex].colors);
					light.dirty = false;
					Lights[index] = light;
					return;
				}

				if (Cache.TryGetValue(light.id, out var cached) && cached.Matches(in light, signature))
				{
					CopyColors(cached, Pool[light.localGlowPoolIndex].colors,
						light.diameter * light.diameter);
					light.dirty = false;
					Lights[index] = light;
					return;
				}

				LeftDirty[index] = true;
			}
		}

		/// <summary>
		/// Exact distance field for a light with no blockers in its rect: the octile distance on an
		/// 8-connected grid with 100/141 costs equals the Dijkstra result, offset by the start cost.
		/// </summary>
		private static unsafe void FillRadial(ref GlowLight light, UnsafeList<Color32> colors)
		{
			NativeArrayUtility.MemClear(colors);
			var dst = colors.Ptr;
			if (dst == null)
			{
				return;
			}

			var radius = light.radius;
			var diameter = light.diameter;
			var glowRadius = light.glowRadius;
			var negInv = -1f / glowRadius;
			var cutoff = Mathf.RoundToInt(glowRadius * 100f);

			for (var lz = 0; lz < diameter; lz++)
			{
				var dz = lz - radius;
				var az = dz < 0 ? -dz : dz;
				var rowBase = lz * diameter;
				for (var lx = 0; lx < diameter; lx++)
				{
					var dx = lx - radius;
					var ax = dx < 0 ? -dx : dx;
					var min = ax < az ? ax : az;
					var max = ax < az ? az : ax;
					var intDist = 100 + 100 * max + 41 * min;
					if (intDist > cutoff)
					{
						continue;
					}

					var dist = intDist / 100f;
					var colorInt = default(ColorInt);
					if (dist <= glowRadius)
					{
						var b = 1f / (dist * dist);
						var factor = Mathf.Lerp(1f + negInv * dist, b, 0.4f);
						colorInt = light.glowColor * factor;
						colorInt.a = 0;
					}
					if (colorInt.r > 0 || colorInt.g > 0 || colorInt.b > 0)
					{
						colorInt.ClampToNonNegative();
						colorInt.a = (int)dist;
						colorInt.ProjectToColor32Fast(out var outColor);
						dst[rowBase + lx] = outColor;
					}
				}
			}
		}

		private static unsafe void CopyColors(CachedGlow cached, UnsafeList<Color32> colors, int count)
		{
			var dst = colors.Ptr;
			if (dst == null || colors.Length < count)
			{
				return;
			}

			var src = cached.Colors;
			for (var i = 0; i < count; i++)
			{
				dst[i] = src[i];
			}
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

				if (state.Lights[i].Colors == null)
				{
					continue;
				}

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
