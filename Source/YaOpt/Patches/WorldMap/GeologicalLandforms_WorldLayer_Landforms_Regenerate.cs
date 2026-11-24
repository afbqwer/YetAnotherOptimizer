using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Prefills an older Geological Landforms tile cache in parallel before world-map drawing.
	/// </summary>
	/// <remarks>
	/// Versions with their own <c>WarmCache</c> are left untouched. The injected path only covers the
	/// first draw after loading; world generation remains serial. During the fill, a per-tile failure
	/// must not invalidate entries already written by other workers.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWorldMapLandformsParallel"/>
	[ManualPatch]
	internal static class GeologicalLandforms_WorldLayer_Landforms_Regenerate
	{
		private static readonly object Sync = new object();

		private static Func<int, bool, object> _getTile;
		private static FieldInfo _cacheField;
		private static FieldInfo _versionField;
		private static MethodInfo _getLandformData;
		private static MethodInfo _invalidateCacheMethod;
		private static Action _invalidateCacheAction;
		private static System.WeakReference<World> _lastWorld;
		private static int _lastVersion = int.MinValue;
		private static bool _active;
		private static bool _guardInstalled;
		private static bool _warned;

		static GeologicalLandforms_WorldLayer_Landforms_Regenerate()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearState);
		}

		internal static bool Patch(Harmony harmony)
		{
			if (!WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapLandformsParallel))
				return true;

			var tileInfo = AccessTools.TypeByName("GeologicalLandforms.WorldTileInfo");
			var layer = AccessTools.TypeByName("GeologicalLandforms.WorldLayer_Landforms");
			if (tileInfo == null || layer == null)
				return true;

			// The locally optimized branch already performs a more complete parallel warm-up.
			if (AccessTools.Method(tileInfo, "WarmCache", Type.EmptyTypes) != null)
				return true;

			var regenerate = AccessTools.Method(layer, "Regenerate", Type.EmptyTypes);
			var get = AccessTools.Method(tileInfo, "Get", new[] { typeof(int), typeof(bool) });
			var invalidate = AccessTools.Method(tileInfo, "InvalidateCache", Type.EmptyTypes);
			_cacheField = AccessTools.Field(tileInfo, "_cache");
			_versionField = AccessTools.Field(tileInfo, "_validCacheVersion");
			_invalidateCacheMethod = invalidate;
			var extensionUtils = AccessTools.TypeByName("GeologicalLandforms.ExtensionUtils");
			_getLandformData = extensionUtils == null ? null :
				AccessTools.Method(extensionUtils, "LandformData", new[] { typeof(World) });
			if (regenerate == null || get == null || invalidate == null || _cacheField == null ||
				_versionField == null || _getLandformData == null)
			{
				return true;
			}

			try
			{
				_getTile = BuildGetTile(get);
				_invalidateCacheAction = AccessTools.MethodDelegate<Action>(invalidate);
			}
			catch (Exception ex)
			{
				WarnOnce("Geological Landforms parallel fill could not be bound: " + ex.Message);
				return true;
			}

			_guardInstalled = false;
			harmony.Patch(regenerate,
				prefix: new HarmonyMethod(typeof(GeologicalLandforms_WorldLayer_Landforms_Regenerate), nameof(Prefix)));
			harmony.Patch(get,
				transpiler: new HarmonyMethod(typeof(GeologicalLandforms_WorldLayer_Landforms_Regenerate), nameof(Transpiler)));
			ClearState();
			return true;
		}

		internal static bool IsAvailable()
		{
			var tileInfo = AccessTools.TypeByName("GeologicalLandforms.WorldTileInfo");
			var layer = AccessTools.TypeByName("GeologicalLandforms.WorldLayer_Landforms");
			if (tileInfo == null || layer == null ||
				AccessTools.Method(tileInfo, "WarmCache", Type.EmptyTypes) != null)
			{
				return false;
			}

			return AccessTools.Method(layer, "Regenerate", Type.EmptyTypes) != null &&
				   AccessTools.Method(tileInfo, "Get", new[] { typeof(int), typeof(bool) }) != null &&
				   AccessTools.Method(tileInfo, "InvalidateCache", Type.EmptyTypes) != null &&
				   AccessTools.Field(tileInfo, "_cache") != null &&
				   AccessTools.Field(tileInfo, "_validCacheVersion") != null &&
				   AccessTools.TypeByName("GeologicalLandforms.ExtensionUtils") != null;
		}

		private static Func<int, bool, object> BuildGetTile(MethodInfo get)
		{
			var id = Expression.Parameter(typeof(int), "id");
			var fromCache = Expression.Parameter(typeof(bool), "fromCache");
			var call = Expression.Call(get, id, fromCache);
			var boxed = Expression.Convert(call, typeof(object));
			return Expression.Lambda<Func<int, bool, object>>(boxed, id, fromCache).Compile();
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = AccessTools.Method(
				typeof(GeologicalLandforms_WorldLayer_Landforms_Regenerate), nameof(InvalidateCacheDuringFill));
			var replaced = false;
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
					method == _invalidateCacheMethod)
				{
					instruction.operand = replacement;
					replaced = true;
				}

				yield return instruction;
			}

			// If another mod changed the error path beyond recognition, keep the serial implementation.
			_guardInstalled = replaced;
		}

		private static void Prefix()
		{
			if (!UnityData.IsInMainThread || !_guardInstalled)
				return;

			lock (Sync)
			{
				if (_active)
					return;

				try
				{
					TryWarm();
				}
				catch (Exception ex)
				{
					WarnOnce("Geological Landforms parallel cache fill failed; using the serial path: " + ex.Message);
				}
			}
		}

		private static void TryWarm()
		{
			var world = Find.World;
			var grid = world?.grid;
			if (grid == null)
				return;

			var count = grid.TilesCount;
			if (count <= 0 || !(_cacheField.GetValue(null) is Array cache) || cache.Length < count)
				return;

			var version = ReadVersion();
			if (_lastWorld != null && _lastWorld.TryGetTarget(out var lastWorld) &&
				ReferenceEquals(lastWorld, world) && _lastVersion == version)
				return;

			// Prime the shared world-component lookup before workers only read it.
			_getLandformData.Invoke(null, new object[] { world });

			var threads = Math.Max(1, Math.Min(Environment.ProcessorCount, 16));
			var chunk = (count + threads - 1) / threads;
			Volatile.Write(ref _active, true);
			try
			{
				var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
				Parallel.For(0, threads, options, worker =>
				{
					var from = worker * chunk;
					var to = Math.Min(count, from + chunk);
					for (var i = from; i < to; i++)
						_getTile(i, true);
				});
			}
			finally
			{
				Volatile.Write(ref _active, false);
			}

			// Entries written under an old version are rejected by Get; let the serial draw rebuild them.
			if (ReadVersion() != version)
				return;

			_lastWorld = new System.WeakReference<World>(world);
			_lastVersion = version;
			YaOptMod.Debug("Geological Landforms prefilled " + count + " world tiles on " + threads + " workers.");
		}

		private static void InvalidateCacheDuringFill()
		{
			if (!Volatile.Read(ref _active))
				_invalidateCacheAction();
		}

		private static int ReadVersion()
		{
			return _versionField.GetValue(null) is int value ? value : 0;
		}

		private static void ClearState()
		{
			_lastWorld = null;
			_lastVersion = int.MinValue;
		}

		private static void WarnOnce(string message)
		{
			if (_warned)
				return;

			_warned = true;
			YaOptMod.Warning(message);
		}
	}
}
