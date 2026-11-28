using HarmonyLib;
using Verse;
using Verse.AI;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Assigns DeepCopy save keys without the quadratic collision retry.
	/// </summary>
	/// <remarks>
	/// The prefix runs last so other patches on the same method keep their say. Returning false skips
	/// the vanilla body only when the call is the DeepCopy path (<c>startHash == 0</c>); the key it
	/// produces is identical to <c>NextUnusedKeyFor</c>.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastThinkKeyAssign"/>
	/// <seealso cref="ThinkKeyAssignFastPath"/>
	[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignSingleKey))]
	[EarlyPatch]
	internal static class Verse_ThinkTreeKeyAssigner_AssignSingleKey
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastThinkKeyAssign.Enabled;
		}

		[HarmonyPriority(Priority.Low)]
		static bool Prefix(ThinkNode node, int startHash)
		{
			if (startHash != 0 || node == null)
				return true;
			node.SetUniqueSaveKey(ThinkKeyAssignFastPath.NextKey(node));
			return false;
		}
	}

	/// <summary>
	/// Drops the skipped-candidate index whenever vanilla clears the assigned key set.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastThinkKeyAssign"/>
	[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.Reset))]
	[EarlyPatch]
	internal static class Verse_ThinkTreeKeyAssigner_Reset
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastThinkKeyAssign.Enabled;
		}

		static void Postfix()
		{
			ThinkKeyAssignFastPath.Reset();
		}
	}
}
