using System.Collections.Generic;
using System.Runtime.InteropServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Ticks a fleck system's game-time list in place instead of through a copied struct.
	/// </summary>
	/// <remarks>
	/// Vanilla <c>FleckSystemBase&lt;T&gt;.Tick</c> copies each fleck out of the list, ticks the copy,
	/// then writes the whole struct back, so every fleck moves roughly twice its own size through
	/// memory each tick (about 160 B for <see cref="FleckStatic"/> and 270 B for
	/// <see cref="FleckThrown"/>). Indexing a span yields a managed reference and a constrained call
	/// on it mutates the element directly, so only the fields <c>TimeInterval</c> actually changes are
	/// written. The observable result is identical: vanilla skips the write-back for flecks that report
	/// removal, and those slots are overwritten by the unordered removal either way.
	/// </remarks>
	/// <seealso cref="Patches.Verse_FleckManager_FleckManagerTick"/>
	internal static class FleckTickHelper
	{
		public static void Tick<TFleck>(FleckSystemBase<TFleck> system) where TFleck : struct, IFleck
		{
			Tick(system.dataGametime, system.tmpRemoveIndices, system.parent.parent);
		}

		public static void Tick<TFleck>(List<TFleck> data, List<int> removeIndices, Map map)
			where TFleck : struct, IFleck
		{
			try
			{
				var span = CollectionsMarshal.AsSpan(data);
				for (var i = span.Length - 1; i >= 0; i--)
				{
					if (span[i].TimeInterval(1f / 60f, map))
						removeIndices.Add(i);
				}
				data.RemoveBatchUnordered(removeIndices);
			}
			finally
			{
				removeIndices.Clear();
			}
		}
	}
}