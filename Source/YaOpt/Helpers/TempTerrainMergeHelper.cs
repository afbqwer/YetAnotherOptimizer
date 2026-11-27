using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Performs the temporary-terrain freeze/melt pass with one temperature read and deferred room
	/// lookups instead of two independent passes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla calls <c>DoWaterFreezing</c> and then <c>DoIceMelting</c> for the same cell. Each reads
	/// the cell temperature and room before checking its temperature guard, so a cell in the normal
	/// temperature band resolves, on average, two temperatures and two rooms for nothing. This
	/// implementation reads the temperature once, only resolves the room after the corresponding
	/// random roll succeeds and runs the terrain check before the room check inside both radial scans.
	/// </para>
	/// <para>
	/// The random stream is preserved: the freezing chance is rolled first and the melting chance
	/// second, under exactly the same conditions as vanilla. When freezing actually changes terrain,
	/// the temperature is re-read before the melting roll because vanilla computes it after the
	/// freezing pass may have changed regions.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptTempTerrainMerge"/>
	internal static class TempTerrainMergeHelper
	{
		private const float FreezeRadius = 2.9f;

		[ThreadStatic] private static List<IntVec3> _cells;

		internal static void DoCell(FreezeManager manager, IntVec3 c)
		{
			Map map = manager.map;
			float temperature = c.GetTemperature(map);
			bool terrainChanged = false;

			// Freezing: vanilla rolls only when the cell is at or below -7 C and can freeze.
			if (!(temperature > -7f) && manager.CanFreeze(c))
			{
				float chance = 0.003f * (temperature / -5f);
				WaterBody waterBody = c.GetWaterBody(map);
				chance += (1f - chance) * Mathf.Pow(
					(float)waterBody.numCellsFrozen / (float)waterBody.numCellsCanFreeze, 10f);
				if (manager.SurroundedByIce(c))
					chance = 1f;

				if (Rand.Chance(chance))
				{
					Room room = c.GetRoom(map);
					List<IntVec3> cells = Cells();
					cells.Clear();
					CollectFreezingCells(manager, map, c, room, cells);

					for (int i = 0; i < cells.Count; i++)
						map.terrainGrid.SetTempTerrain(cells[i], TerrainDefOf.ThinIce);

					terrainChanged = cells.Count > 0;
				}
			}

			// Vanilla computes the melting temperature after the freezing pass may have changed regions.
			if (terrainChanged)
				temperature = c.GetTemperature(map);

			// Melting: vanilla rolls only when the cell is at or above 2 C.
			if (!(temperature < 2f) && Rand.Chance(0.001f * ((temperature + 5f) / 5f)))
			{
				Room room = c.GetRoom(map);
				List<IntVec3> cells = Cells();
				cells.Clear();
				CollectMeltingCells(manager, map, c, room, cells);

				for (int i = 0; i < cells.Count; i++)
					map.terrainGrid.RemoveTempTerrain(cells[i]);
			}
		}

		private static List<IntVec3> Cells()
		{
			List<IntVec3> cells = _cells;
			if (cells == null)
			{
				cells = new List<IntVec3>();
				_cells = cells;
			}
			return cells;
		}

		/// <summary>
		/// Same cells as vanilla's <c>CanFreeze(item)</c> filter, but the cheap terrain check runs
		/// before the region lookup. <c>CanFreeze</c> is <c>CanEverFreeze &amp;&amp; !SurroundedByWater</c>.
		/// </summary>
		private static void CollectFreezingCells(FreezeManager manager, Map map, IntVec3 center,
			Room room, List<IntVec3> cells)
		{
			int count = GenRadial.NumCellsInRadius(FreezeRadius);
			for (int i = 0; i < count; i++)
			{
				IntVec3 cell = center + GenRadial.RadialPattern[i];
				if (!cell.InBounds(map) || !FreezeManager.CanEverFreeze(cell, map))
					continue;

				if (cell.GetRoom(map) != room || manager.SurroundedByWater(cell))
					continue;

				cells.Add(cell);
			}
		}

		/// <summary>
		/// Same cells as vanilla's <c>CanEverMelt(item) &amp;&amp; CanMelt(item)</c> filter, with the
		/// terrain check first. <c>CanMelt</c> is <c>CanEverMelt &amp;&amp; !SurroundedByIce</c>.
		/// </summary>
		private static void CollectMeltingCells(FreezeManager manager, Map map, IntVec3 center,
			Room room, List<IntVec3> cells)
		{
			int count = GenRadial.NumCellsInRadius(FreezeRadius);
			for (int i = 0; i < count; i++)
			{
				IntVec3 cell = center + GenRadial.RadialPattern[i];
				if (!cell.InBounds(map) || !manager.CanEverMelt(cell))
					continue;

				if (cell.GetRoom(map) != room || manager.SurroundedByIce(cell))
					continue;

				cells.Add(cell);
			}
		}
	}
}