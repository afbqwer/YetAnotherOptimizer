using System;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Memoises the Beauty and BeautyOutdoors values of every <see cref="TerrainDef"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="RimWorld.BeautyUtility.CellBeauty"/> asks for the terrain's abstract Beauty value
	/// once per sampled cell. An abstract request has no <see cref="Thing"/>, so vanilla's
	/// <c>StatWorker</c> caches cannot help; every call walks the full stat pipeline again.
	/// </para>
	/// <para>
	/// Terrain defs do not change during play, so the values are stored in flat arrays indexed by
	/// <see cref="Def.index"/>. This follows <see cref="Patches.ThingDefBaseStatCache"/>: a mod that
	/// rewrites a terrain def's stat bases at runtime will see the cached value until the next load.
	/// </para>
	/// </remarks>
	/// <seealso cref="Patches.RimWorld_StatWorker_GetValueAbstract_TerrainBeautyCache"/>
	/// <seealso cref="YaOptSettings.OptTerrainBeautyCache"/>
	internal static class TerrainBeautyCache
	{
		private static float[] _beauty;

		private static float[] _beautyOutdoors;

		/// <summary>
		/// Called from the patch's <c>Prepare</c>, i.e. once at startup and again on each game load.
		/// </summary>
		internal static void Invalidate()
		{
			_beauty = null;
			_beautyOutdoors = null;
		}

		private static int EnsureArrays()
		{
			// Before a game exists the def list is still being edited, so caching here would freeze
			// intermediate values.
			if (Current.Game == null)
				return 0;

			var count = DefDatabase<TerrainDef>.DefCount;
			if (count == 0)
				return 0;

			var beauty = _beauty;
			var outdoors = _beautyOutdoors;
			if (beauty != null && beauty.Length == count && outdoors != null && outdoors.Length == count)
				return count;

			beauty = new float[count];
			outdoors = new float[count];
			Array.Fill(beauty, float.NaN);
			Array.Fill(outdoors, float.NaN);
			// Publish the outdoor array first: a reader that sees a new beauty array also sees the
			// matching outdoor array.
			_beautyOutdoors = outdoors;
			_beauty = beauty;
			return count;
		}

		internal static bool TryGet(TerrainDef def, bool outdoors, out float value)
		{
			var cache = outdoors ? _beautyOutdoors : _beauty;
			if (cache == null && EnsureArrays() == 0)
			{
				value = 0f;
				return false;
			}

			cache = outdoors ? _beautyOutdoors : _beauty;
			if (cache == null)
			{
				value = 0f;
				return false;
			}

			var index = def.index;
			if ((uint)index >= (uint)cache.Length)
			{
				value = 0f;
				return false;
			}

			value = cache[index];
			return !float.IsNaN(value);
		}

		internal static void Store(TerrainDef def, bool outdoors, float value)
		{
			var cache = outdoors ? _beautyOutdoors : _beauty;
			if (cache == null && EnsureArrays() == 0)
				return;

			cache = outdoors ? _beautyOutdoors : _beauty;
			if (cache == null)
				return;

			var index = def.index;
			if ((uint)index < (uint)cache.Length)
				cache[index] = value;
		}
	}
}