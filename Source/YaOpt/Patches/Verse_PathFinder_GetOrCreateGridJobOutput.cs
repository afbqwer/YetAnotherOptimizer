using HarmonyLib;
using Unity.Collections;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Allocates recycled path-grid buffers without the redundant whole-map zero fill.
	/// </summary>
	/// <remarks>
	/// <see cref="PathFinder.GridJobOutput.Initalize"/> creates the buffer with
	/// <see cref="NativeArrayOptions.ClearMemory"/>, but <see cref="PathGridJob"/> writes every index in
	/// <c>[0, NumGridCells)</c> it was scheduled for, so the clear is pure overhead on each new key. Pooled
	/// buffers are reused as-is and fully overwritten in the same way.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPathGridOutputUninit"/>
	[HarmonyPatch(typeof(PathFinder))]
	[HarmonyPatch(nameof(PathFinder.GetOrCreateGridJobOutput))]
	internal static class Verse_PathFinder_GetOrCreateGridJobOutput
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPathGridOutputUninit.Enabled;
		}

		static bool Prefix(PathFinder __instance, out PathFinder.GridJobOutput output)
		{
			if (__instance.map == null)
			{
				output = default;
				return true;
			}

			if (!__instance.gridJobOutputs.TryDequeue(out output))
			{
				output = default;
				output.grid = new NativeArray<int>(__instance.map.cellIndices.NumGridCells,
					Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}
			return false;
		}
	}
}