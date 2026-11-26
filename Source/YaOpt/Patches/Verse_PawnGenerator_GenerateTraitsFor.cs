using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Opens and closes the trait candidate session around
	/// <see cref="PawnGenerator.GenerateTraitsFor"/>.
	/// </summary>
	/// <remarks>
	/// The session only records the pawn, so HAR's validator patch can reuse one validated candidate list
	/// for every weighted draw inside the call. The session is closed in a finalizer so every early return
	/// releases it.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnTraitsHarFastPath"/>
	/// <seealso cref="HarTraitCandidates"/>
	[HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GenerateTraitsFor))]
	internal static class Verse_PawnGenerator_GenerateTraitsFor
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnTraitsHarFastPath.Enabled == true
				&& HarTraitCandidates.Ready;
		}

		static void Prefix(Pawn pawn, out bool __state)
		{
			__state = HarTraitCandidates.Begin(pawn);
		}

		static void Finalizer(bool __state)
		{
			if (__state)
				HarTraitCandidates.End();
		}
	}
}