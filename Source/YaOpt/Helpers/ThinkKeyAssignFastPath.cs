using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces the retry loop of <c>ThinkTreeKeyAssigner.NextUnusedKeyFor</c> for the DeepCopy path
	/// (<c>AssignSingleKey(node, 0)</c>).
	/// </summary>
	/// <remarks>
	/// On that path the copied node has no parent yet, so every node of the same type starts from the
	/// same candidate. The vanilla loop advances <c>Rand</c> one step per retry and every call restarts
	/// it from the same seed, so the k-th node of a type walks the same k-1 candidates again and the
	/// pass becomes quadratic in the number of nodes per type (about 43 s with the installed mods).
	///
	/// <c>Rand.Int</c> is a pure function of its seed and iteration index, so the candidate values are
	/// rebuilt from <see cref="MurmurHash"/> instead of pulling from <c>Rand</c>. Remembering the next
	/// index per candidate base skips the already-taken prefix and keeps every assigned key identical
	/// to vanilla, because <c>Job.lastJobGiverKey</c> is saved and resolved against them on load.
	/// </remarks>
	internal static class ThinkKeyAssignFastPath
	{
		/// <summary>Next candidate index to test for a given base key.</summary>
		private static readonly Dictionary<int, int> NextIndex = new Dictionary<int, int>();

		/// <summary>Cumulative XOR of the Rand sequence for seed 0, so candidate(n) = base ^ table[n-1].</summary>
		private static int[] _cumulativeXor;

		internal static void Reset()
		{
			NextIndex.Clear();
			_cumulativeXor = null;
		}

		/// <summary>Same result as <c>NextUnusedKeyFor</c>, without re-walking the taken candidates.</summary>
		internal static int NextKey(ThinkNode node)
		{
			var keys = ThinkTreeKeyAssigner.assignedKeys;
			var key = 0;
			for (var current = node; current != null; current = current.parent)
				key = Gen.HashCombineInt(key, GenText.StableStringHash(current.GetType().Name));

			if (!keys.Contains(key))
			{
				keys.Add(key);
				return key;
			}

			var index = 1;
			NextIndex.TryGetValue(key, out var stored);
			if (stored > index)
				index = stored;
			while (true)
			{
				var candidate = key ^ CumulativeXor(index);
				if (!keys.Contains(candidate))
				{
					keys.Add(candidate);
					NextIndex[key] = index + 1;
					return candidate;
				}
				index++;
			}
		}

		/// <summary>XOR of <c>Rand.Int</c> values 0..count-1 after <c>Rand.PushState(0)</c>.</summary>
		private static int CumulativeXor(int count)
		{
			var cache = _cumulativeXor;
			if (cache == null || cache.Length < count)
			{
				var size = cache == null ? 64 : cache.Length * 2;
				while (size < count)
					size *= 2;
				cache = new int[size];
				var cumulative = 0;
				for (var i = 0; i < size; i++)
				{
					cumulative ^= MurmurHash.GetInt(0u, (uint)i);
					cache[i] = cumulative;
				}
				_cumulativeXor = cache;
			}
			return cache[count - 1];
		}
	}
}
