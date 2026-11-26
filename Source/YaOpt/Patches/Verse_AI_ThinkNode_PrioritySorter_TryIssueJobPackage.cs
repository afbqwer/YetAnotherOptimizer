using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces <see cref="ThinkNode_PrioritySorter.TryIssueJobPackage"/> with a version that computes
	/// each child's priority once per call.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla recomputes the priority of every remaining child after each failed child, so an idle
	/// pawn with fourteen children performs 14+13+… getter calls. A priority cannot change while one
	/// selection pass runs, so a snapshot keeps the same order and calls each getter once.
	/// </para>
	/// <para>
	/// The randomised insertion order and the first-strict-maximum tie break are preserved, and each
	/// child's exception is logged exactly as in vanilla. Buffers are per thread and per nesting
	/// depth, so a child that re-enters selection (for example through
	/// <c>CheckForJobOverride</c>) cannot corrupt its caller's list. Beyond the maximum depth the
	/// vanilla implementation runs instead.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPrioritySorterSnapshot"/>
	[HarmonyPatch(typeof(ThinkNode_PrioritySorter), nameof(ThinkNode_PrioritySorter.TryIssueJobPackage))]
	internal static class Verse_AI_ThinkNode_PrioritySorter_TryIssueJobPackage
	{
		private const int MaxDepth = 4;

		[ThreadStatic] private static List<ThinkNode>[] _nodeBuffers;

		[ThreadStatic] private static List<float>[] _priorityBuffers;

		[ThreadStatic] private static int _depth;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPrioritySorterSnapshot.Enabled == true;
		}

		// Run after foreign prefixes so an external short-circuit still wins.
		[HarmonyPriority(Priority.VeryLow)]
		static bool Prefix(ThinkNode_PrioritySorter __instance, Pawn pawn, JobIssueParams jobParams,
			ref ThinkResult __result)
		{
			var depth = _depth;
			if (depth >= MaxDepth)
				return true;

			_depth = depth + 1;
			try
			{
				var nodes = GetBuffer(ref _nodeBuffers, depth);
				var priorities = GetBuffer(ref _priorityBuffers, depth);
				nodes.Clear();
				priorities.Clear();

				var count = __instance.subNodes.Count;
				for (var i = 0; i < count; i++)
				{
					// Same quirky range as vanilla: the last slot is never hit.
					nodes.Insert(Rand.Range(0, nodes.Count - 1), __instance.subNodes[i]);
				}

				for (var i = 0; i < nodes.Count; i++)
				{
					var priority = 0f;
					try
					{
						priority = nodes[i].GetPriority(pawn);
					}
					catch (Exception ex)
					{
						Log.Error("Exception in " + __instance.GetType()?.ToString() + " GetPriority: "
							+ ex.ToString());
					}
					priorities.Add(priority);
				}

				var minPriority = __instance.minPriority;
				while (nodes.Count > 0)
				{
					var bestIndex = -1;
					var bestPriority = 0f;
					for (var i = 0; i < nodes.Count; i++)
					{
						var priority = priorities[i];
						if (priority > 0f && priority >= minPriority && priority > bestPriority)
						{
							bestPriority = priority;
							bestIndex = i;
						}
					}

					if (bestIndex < 0)
						break;

					var result = ThinkResult.NoJob;
					try
					{
						result = nodes[bestIndex].TryIssueJobPackage(pawn, jobParams);
					}
					catch (Exception ex)
					{
						Log.Error("Exception in " + __instance.GetType()?.ToString()
							+ " TryIssueJobPackage: " + ex.ToString());
					}

					if (result.IsValid)
					{
						__result = result;
						return false;
					}

					nodes.RemoveAt(bestIndex);
					priorities.RemoveAt(bestIndex);
				}

				__result = ThinkResult.NoJob;
				return false;
			}
			catch (Exception ex)
			{
				// Let vanilla handle the call if the snapshot itself fails.
				Log.Error("YaOpt priority sorter snapshot failed: " + ex.ToString());
				return true;
			}
			finally
			{
				_depth = depth;
			}
		}

		private static List<T> GetBuffer<T>(ref List<T>[] buffers, int depth)
		{
			if (buffers == null)
				buffers = new List<T>[MaxDepth];

			var list = buffers[depth];
			if (list == null)
			{
				list = new List<T>();
				buffers[depth] = list;
			}
			return list;
		}
	}
}