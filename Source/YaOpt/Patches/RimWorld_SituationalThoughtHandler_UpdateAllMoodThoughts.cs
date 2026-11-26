using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips a situational thought rebuild that repeats within the same tick.
	/// </summary>
	/// <remarks>
	/// One <see cref="Need_Mood.NeedInterval"/> can rebuild situational thoughts twice: the first
	/// call comes from <c>CurInstantLevel</c> when <c>thoughtsDirty</c> is set, the second from
	/// <c>SituationalThoughtInterval</c> unconditionally. The second call runs immediately after the
	/// first and sees identical data, so it is skipped while the dirty flag stays clear. Sleeping
	/// pawns and pawns whose instant mood was already evaluated this tick only take the second path
	/// and are unaffected.
	/// </remarks>
	/// <seealso cref="ThoughtUpdateCache"/>
	/// <seealso cref="YaOptSettings.OptMoodThoughtDedup"/>
	[HarmonyPatch(typeof(SituationalThoughtHandler), "UpdateAllMoodThoughts")]
	internal static class RimWorld_SituationalThoughtHandler_UpdateAllMoodThoughts
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptMoodThoughtDedup.Enabled == true
				&& AccessTools.Method(typeof(SituationalThoughtHandler), "UpdateAllMoodThoughts") != null
				&& AccessTools.Field(typeof(SituationalThoughtHandler), "thoughtsDirty") != null;
		}

		static bool Prefix(SituationalThoughtHandler __instance, bool ___thoughtsDirty)
		{
			return ThoughtUpdateCache.ShouldUpdate(__instance, ___thoughtsDirty);
		}
	}
}