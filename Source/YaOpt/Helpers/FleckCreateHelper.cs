using System.Runtime.InteropServices;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Builds a new fleck directly inside its system's list instead of on the stack and then copying it in.
	/// </summary>
	/// <remarks>
	/// Vanilla <c>FleckSystemBase&lt;T&gt;.CreateFleck</c> constructs the struct in a local, runs
	/// <c>Setup</c> on that local and then appends a copy, so every spawned fleck of 160-270 B is written
	/// twice. Growing the list first and running <c>Setup</c> on the new element produces the same
	/// fields without the extra copy. The slot is zeroed first because <c>List&lt;T&gt;</c> only clears
	/// removed tail slots for types that contain references, which <see cref="FleckStatic"/> and
	/// <see cref="FleckThrown"/> do, but relying on that would be an implementation detail.
	/// </remarks>
	/// <seealso cref="Patches.Verse_FleckManager_CreateFleck"/>
	internal static class FleckCreateHelper
	{
		public static void Create<TFleck>(FleckSystemBase<TFleck> system, FleckCreationData creationData)
			where TFleck : struct, IFleck
		{
			var data = creationData.def.realTime ? system.dataRealtime : system.dataGametime;
			var index = data.Count;
			CollectionsMarshal.SetCount(data, index + 1);
			var span = CollectionsMarshal.AsSpan(data);
			span[index] = default;
			span[index].Setup(creationData);
		}
	}
}