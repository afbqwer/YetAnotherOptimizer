using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Per-NativeArray scratch bookkeeping for the sparse "clear only what was written" replacements
	/// of the per-path full array clears in the vanilla path jobs.
	/// </summary>
	/// <remarks>
	/// Keyed by the unmanaged buffer pointer. A pointer can be reused by a differently sized allocation
	/// after a map change, so <see cref="Length"/> is validated; on a mismatch the caller performs a
	/// one-off full clear and tracking restarts (resetting already-zero memory is a no-op, so stale
	/// pointers from a same-sized reuse are harmless).
	/// </remarks>
	internal sealed class ScratchTouch
	{
		public readonly List<int> Indices = new List<int>();

		public int Length = -1;

		public bool Initialized;
	}

	internal static class ScratchTouchRegistry
	{
		private static readonly ConcurrentDictionary<IntPtr, ScratchTouch> _map =
			new ConcurrentDictionary<IntPtr, ScratchTouch>();

		/// <summary>
		/// Returns the scratch entry for <paramref name="array"/>. When <paramref name="fullClearRequired"/>
		/// is true the caller must clear the whole array once (first use or a changed allocation size).
		/// </summary>
		internal static unsafe ScratchTouch Get<T>(NativeArray<T> array, out bool fullClearRequired) where T : struct
		{
			var ptr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(array);
			var length = array.Length;
			var touch = _map.GetOrAdd(ptr, _ => new ScratchTouch());
			if (!touch.Initialized || touch.Length != length)
			{
				touch.Initialized = true;
				touch.Length = length;
				touch.Indices.Clear();
				fullClearRequired = true;
			}
			else
			{
				fullClearRequired = false;
			}
			return touch;
		}

		/// <summary>
		/// Drops every entry, e.g. when a <see cref="PathFinder"/> is disposed and its buffers are freed.
		/// </summary>
		/// <remarks>
		/// Safe to call while jobs run on other maps: a buffer is only ever used by one in-flight job at a
		/// time, and a entry created after the clear starts with <c>Initialized == false</c>, so the first
		/// run following the clear still performs a full clear and cannot inherit stale values from a write
		/// whose bookkeeping was discarded.
		/// </remarks>
		internal static void Clear()
		{
			_map.Clear();
		}
	}
}