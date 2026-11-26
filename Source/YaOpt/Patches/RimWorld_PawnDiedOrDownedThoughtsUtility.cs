using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the Died branch of <see cref="PawnDiedOrDownedThoughtsUtility.TryGiveThoughts"/>.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDeathThoughts"/>
	[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
	[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.TryGiveThoughts),
		new[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
	internal static class RimWorld_PawnDiedOrDownedThoughtsUtility_TryGiveThoughts
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptDeathThoughts.Enabled;
		}

		static bool Prefix(Pawn victim, DamageInfo? dinfo,
			PawnDiedOrDownedThoughtsKind thoughtsKind)
		{
			if (thoughtsKind != PawnDiedOrDownedThoughtsKind.Died)
				return true;

			DeathThoughtsHelper.TryGiveDiedThoughts(victim, dinfo);
			return false;
		}
	}

	/// <seealso cref="YaOptSettings.OptDeathThoughts"/>
	[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
	[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.RemoveLostThoughts))]
	internal static class RimWorld_PawnDiedOrDownedThoughtsUtility_RemoveLostThoughts
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptDeathThoughts.Enabled;
		}

		static bool Prefix(Pawn pawn)
		{
			DeathThoughtsHelper.RemoveLostThoughts(pawn);
			return false;
		}
	}

	/// <seealso cref="YaOptSettings.OptDeathThoughts"/>
	[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
	[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.RemoveDiedThoughts))]
	internal static class RimWorld_PawnDiedOrDownedThoughtsUtility_RemoveDiedThoughts
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptDeathThoughts.Enabled;
		}

		static bool Prefix(Pawn pawn)
		{
			// A pawn that never died cannot have death memories about itself. Freshly spawned
			// raiders dominate this call site, so skipping them removes a full map scan per pawn.
			if (!DeathThoughtsHelper.MayHaveDeathMemories(pawn))
				return false;

			DeathThoughtsHelper.RemoveDiedThoughts(pawn);
			DeathThoughtsHelper.ClearDeathMemoryOwner(pawn);
			return false;
		}
	}

	/// <seealso cref="YaOptSettings.OptDeathThoughts"/>
	[HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility))]
	[HarmonyPatch(nameof(PawnDiedOrDownedThoughtsUtility.RemoveResuedRelativeThought))]
	internal static class RimWorld_PawnDiedOrDownedThoughtsUtility_RemoveResuedRelativeThought
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptDeathThoughts.Enabled;
		}

		static bool Prefix(Pawn pawn)
		{
			DeathThoughtsHelper.RemoveResuedRelativeThought(pawn);
			return false;
		}
	}
}