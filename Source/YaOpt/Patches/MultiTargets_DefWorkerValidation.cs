using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Stand-in for an <see cref="IncidentDef"/> whose <c>workerClass</c> is missing or cannot be
	/// instantiated. Never fires, never throws.
	/// </summary>
	internal sealed class IncidentWorker_AlwaysSkip : IncidentWorker
	{
		public override float BaseChanceThisGame => 0f;

		public override float ChanceFactorNow(IIncidentTarget target) => 0f;

		public override bool CanFireNowSub(IncidentParms parms) => true;

		public override bool TryExecuteWorker(IncidentParms parms) => true;
	}

	/// <summary>
	/// Stand-in for a <see cref="WorkGiverDef"/> whose <c>giverClass</c> is missing or cannot be
	/// instantiated. Skips every pawn.
	/// </summary>
	internal sealed class WorkGiver_AlwaysSkip : WorkGiver
	{
		public override bool ShouldSkip(Pawn pawn, bool forced = false) => true;
	}

	/// <summary>
	/// Validates <see cref="IncidentDef.Worker"/> before the game tries to instantiate it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Neither <c>IncidentDef.ConfigErrors</c> nor <c>WorkGiverDef.ConfigErrors</c> validates the worker
	/// type in 1.7, so a def with a missing or unresolvable class turns into an
	/// <see cref="ArgumentNullException"/> the moment the storyteller rolls that incident. That happens
	/// inside a tick, so the exception repeats every attempt and floods the log.
	/// </para>
	/// <para>
	/// The prefix covers a null class; the finalizer covers a class that exists but cannot be
	/// constructed (abstract type, missing dependency, throwing constructor). Both replace the def with
	/// a no-op worker instead of letting the failure escape. This changes behaviour on purpose: a
	/// broken def becomes inert rather than fatal, and the log gets one line naming the mod.
	/// </para>
	/// </remarks>
	[HarmonyPatch(typeof(IncidentDef), nameof(IncidentDef.Worker), MethodType.Getter)]
	internal static class RimWorld_IncidentDef_Worker
	{
		private const int LogKeyBase = 0x5A170000;

		static void Prefix(IncidentDef __instance)
		{
			if (__instance == null || __instance.workerInt != null || __instance.workerClass != null)
				return;
			__instance.workerClass = typeof(IncidentWorker_AlwaysSkip);
			Log.ErrorOnce(
				$"IncidentDef '{(__instance.defName ?? "null")}' from mod "
				+ $"'{(__instance.modContentPack?.Name ?? "null")}' has no workerClass. "
				+ "YaOpt disabled it to stop the error spam.",
				LogKeyBase + __instance.index);
		}

		static Exception Finalizer(IncidentDef __instance, Exception __exception, ref IncidentWorker __result)
		{
			if (__exception == null)
				return null;
			// Only swallow the failure to construct the worker. If workerInt is already set the exception
			// came from somewhere else (another mod's postfix, for example) and is not ours to hide.
			if (__instance == null || __instance.workerInt != null)
				return __exception;

			var fallback = new IncidentWorker_AlwaysSkip { def = __instance };
			__instance.workerClass = typeof(IncidentWorker_AlwaysSkip);
			__instance.workerInt = fallback;
			__result = fallback;
			Log.ErrorOnce(
				$"IncidentDef '{(__instance.defName ?? "null")}' from mod "
				+ $"'{(__instance.modContentPack?.Name ?? "null")}' could not instantiate its worker. "
				+ $"YaOpt disabled it to stop the error spam. {__exception}",
				LogKeyBase + __instance.index);
			return null;
		}
	}

	/// <summary>
	/// Validates <see cref="WorkGiverDef.Worker"/>. Same reasoning and shape as
	/// <see cref="RimWorld_IncidentDef_Worker"/>; the failure there happens while a pawn looks for a job,
	/// which is even hotter.
	/// </summary>
	[HarmonyPatch(typeof(WorkGiverDef), nameof(WorkGiverDef.Worker), MethodType.Getter)]
	internal static class RimWorld_WorkGiverDef_Worker
	{
		private const int LogKeyBase = 0x5A180000;

		static void Prefix(WorkGiverDef __instance)
		{
			if (__instance == null || __instance.workerInt != null || __instance.giverClass != null)
				return;
			__instance.giverClass = typeof(WorkGiver_AlwaysSkip);
			Log.ErrorOnce(
				$"WorkGiverDef '{(__instance.defName ?? "null")}' from mod "
				+ $"'{(__instance.modContentPack?.Name ?? "null")}' has no giverClass. "
				+ "YaOpt disabled it to stop the error spam.",
				LogKeyBase + __instance.index);
		}

		static Exception Finalizer(WorkGiverDef __instance, Exception __exception, ref WorkGiver __result)
		{
			if (__exception == null)
				return null;
			// Same rule as the incident worker: only a failed construction is swallowed.
			if (__instance == null || __instance.workerInt != null)
				return __exception;

			var fallback = new WorkGiver_AlwaysSkip { def = __instance };
			__instance.giverClass = typeof(WorkGiver_AlwaysSkip);
			__instance.workerInt = fallback;
			__result = fallback;
			Log.ErrorOnce(
				$"WorkGiverDef '{(__instance.defName ?? "null")}' from mod "
				+ $"'{(__instance.modContentPack?.Name ?? "null")}' could not instantiate its worker. "
				+ $"YaOpt disabled it to stop the error spam. {__exception}",
				LogKeyBase + __instance.index);
			return null;
		}
	}
}
