using HarmonyLib;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Per-pawn packed invisibility state, published by the pawn's own tick and read lock-free
	/// from render, AI and UI code.
	/// </summary>
	/// <remarks>
	/// Vanilla answers every invisibility query by walking the pawn's hediff list. The answer
	/// depends on three things: whether the pawn carries an <see cref="HediffComp_Invisibility"/>
	/// (structural, changes only on add/remove), the fade anchor and direction (a pure function of
	/// <c>TicksGame</c> and two mind-state ticks), and <c>ForcedVisible</c> (time and state based,
	/// but already evaluated unconditionally by <c>CompPostTick</c> every tick).
	///
	/// So the cache does not introduce a new evaluation schedule: it republishes the result vanilla
	/// already computed into one 8-byte field. Readers then do a single aligned load instead of a
	/// hediff list scan plus a rescan of <c>Pawn.AllComps</c> for the holding platform.
	///
	/// The field is injected by the built-in Prepatcher and defaults to zero, which means
	/// "no invisibility comp". That is also the correct answer for freshly created pawns, for
	/// pawns restored from a save that predates the field, and for anything the injections missed.
	/// A zero value therefore never causes a wrong answer, only a missed optimization.
	/// </remarks>
	public static class PawnInvisibilityCache
	{
		internal const long HasComp = 1L << 0;

		internal const long MultiComp = 1L << 1;

		internal const long CustomComp = 1L << 2;

		internal const long ForcedVisible = 1L << 3;

		internal const long VisibleToPlayer = 1L << 4;

		internal const long FadeInDirection = 1L << 5;

		internal const long DurationOverflow = 1L << 6;

		private const int DurationShift = 8;

		private const long DurationMask = 0xFFFFFFL;

		private const int AnchorShift = 32;

		private static SimpleCurve _revealCurve;

		private static Func<HediffComp_Invisibility, bool> _forcedVisibleGetter;

		private static AccessTools.FieldRef<HediffComp_Invisibility, bool> _wasForcedVisibleField;

		private static bool _resolved;

		private static bool _resolveAttempted;

		/// <summary>
		/// True when the reveal curve used by <c>GetAlpha</c> was located, so the alpha path can be
		/// answered from the packed state instead of falling back to vanilla.
		/// </summary>
		internal static bool RevealCurveAvailable => _revealCurve != null;

		/// <summary>
		/// Resolves the private vanilla members the cache needs. Runs once; the result is cached.
		/// </summary>
		internal static bool Resolve()
		{
			if (_resolveAttempted)
				return _resolved;
			_resolveAttempted = true;
			try
			{
				var curveField = AccessTools.Field(typeof(HediffComp_Invisibility), "RevealCurve");
				_revealCurve = curveField?.GetValue(null) as SimpleCurve;
				if (_revealCurve == null)
					YaOptMod.Warning("Invisibility cache: RevealCurve not found, GetAlpha stays on vanilla.");

				var getter = AccessTools.PropertyGetter(typeof(HediffComp_Invisibility),
					"ForcedVisible");
				if (getter != null)
					_forcedVisibleGetter =
						(Func<HediffComp_Invisibility, bool>)Delegate.CreateDelegate(
							typeof(Func<HediffComp_Invisibility, bool>), getter);
				else
					YaOptMod.Warning("Invisibility cache: ForcedVisible getter not found.");

				_wasForcedVisibleField =
					AccessTools.FieldRefAccess<HediffComp_Invisibility, bool>(
						"wasForcedVisibleLastTick");

				_resolved = _forcedVisibleGetter != null && _wasForcedVisibleField != null;
				if (!_resolved)
					YaOptMod.Warning("Invisibility cache disabled: required members are missing.");
			}
			catch (Exception ex)
			{
				YaOptMod.Error("Invisibility cache: failed to resolve vanilla members.\n" + ex);
				_resolved = false;
			}
			return _resolved;
		}

		/// <summary>
		/// The value <c>CompPostTick</c> assigned to <c>wasForcedVisibleLastTick</c> this tick,
		/// which is exactly this tick's <c>ForcedVisible</c>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool WasForcedVisible(HediffComp_Invisibility comp)
		{
			return _wasForcedVisibleField(comp);
		}

		/// <summary>
		/// Evaluates the vanilla predicate. Only for low-frequency paths; the per-tick path reads
		/// the value vanilla already stored instead.
		/// </summary>
		internal static bool ForcedVisibleOf(HediffComp_Invisibility comp)
		{
			return _forcedVisibleGetter(comp);
		}

		/// <summary>
		/// Republishes the state from the per-tick path. Structural bits are inherited from the
		/// last <see cref="Rebuild"/> so this does not pay for the comp count or subclass check.
		/// </summary>
		/// <remarks>
		/// Only valid from the tail of <c>CompPostTick</c>: that method assigns
		/// <c>wasForcedVisibleLastTick = ForcedVisible</c> unconditionally, so the field holds this
		/// tick's predicate result. A subclass may override <c>CompPostTick</c> without calling
		/// base, in which case the field never advances; the subclass bit recorded by
		/// <see cref="Rebuild"/> selects direct evaluation there instead. The visibility predicates
		/// are not virtual, so a subclass cannot change what the cached answer means.
		/// </remarks>
		internal static void Publish(HediffComp_Invisibility comp)
		{
			PublishCore(comp, false);
		}

		/// <summary>
		/// Republishes after an explicit state switch, where <c>wasForcedVisibleLastTick</c> is
		/// still the previous tick's value and the predicate must be evaluated directly.
		/// </summary>
		internal static void PublishEvaluated(HediffComp_Invisibility comp)
		{
			PublishCore(comp, true);
		}

		private static void PublishCore(HediffComp_Invisibility comp, bool forceEvaluate)
		{
			var pawn = comp.parent?.pawn;
			if (pawn == null)
				return;
			long previous = Volatile.Read(ref PrepatcherFields.Invisibility(pawn));
			if ((previous & HasComp) == 0L)
			{
				// No structural pass has run for this pawn yet. Rebuild instead of inheriting:
				// publishing here would otherwise assert "no invisibility comp" permanently,
				// because the presence bit is only ever set by Rebuild.
				Rebuild(pawn);
				return;
			}
			bool forced = forceEvaluate || (previous & CustomComp) != 0L
				? ForcedVisibleOf(comp)
				: WasForcedVisible(comp);
			long bits = Build(comp, forced, previous & (MultiComp | CustomComp));
			Volatile.Write(ref PrepatcherFields.Invisibility(pawn), bits);
		}

		/// <summary>
		/// Recomputes every bit from scratch. Used on structural changes, spawn and load.
		/// </summary>
		internal static void Rebuild(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null)
				return;

			var hediffs = pawn.health.hediffSet.hediffs;
			HediffComp_Invisibility first = null;
			var count = 0;
			for (var i = 0; i < hediffs.Count; i++)
			{
				var comp = hediffs[i].TryGetComp<HediffComp_Invisibility>();
				if (comp == null)
					continue;
				if (first == null)
					first = comp;
				count++;
			}

			if (first == null)
			{
				Volatile.Write(ref PrepatcherFields.Invisibility(pawn), 0L);
				return;
			}

			long structural = count > 1 ? MultiComp : 0L;
			if (first.GetType() != typeof(HediffComp_Invisibility))
				structural |= CustomComp;

			long bits = Build(first, ForcedVisibleOf(first), structural);
			Volatile.Write(ref PrepatcherFields.Invisibility(pawn), bits);
		}

		/// <summary>
		/// Answers a read request. Returns false when the caller must fall back to vanilla:
		/// multiple comps (vanilla takes the first for alpha but any for the boolean queries, and
		/// their disruption windows are independent, so one packed slot cannot represent them),
		/// a fade duration that does not fit the packed field, or a pawn that does not tick.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryRead(Pawn pawn, out long bits)
		{
			bits = Volatile.Read(ref PrepatcherFields.Invisibility(pawn));
			if ((bits & HasComp) == 0L)
				return true;
			if ((bits & (MultiComp | DurationOverflow)) != 0L)
				return false;
			// CompPostTick only runs for pawns whose Tick() executes. Carried and contained pawns
			// take the TickInterval path, and a carried invisible pawn is still drawn every frame.
			return pawn.Spawned;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool IsPsychologicallyVisible(long bits)
		{
			if ((bits & ForcedVisible) != 0L)
				return true;
			return FadePct(bits) > 0f;
		}

		internal static float Alpha(long bits)
		{
			if ((bits & (VisibleToPlayer | ForcedVisible)) != 0L)
				return 1f;
			return _revealCurve.Evaluate(FadePct(bits));
		}

		/// <summary>
		/// Mirrors <c>HediffComp_Invisibility.FadePct</c> exactly, including the division by a
		/// zero duration, so the cached answer stays bit-identical to vanilla.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static float FadePct(long bits)
		{
			var now = Find.TickManager.TicksGame;
			var anchor = (int)(bits >> AnchorShift);
			var duration = (int)((bits >> DurationShift) & DurationMask);
			float pct = Mathf.Clamp01((float)(now - anchor) / (float)duration);
			return (bits & FadeInDirection) != 0L ? pct : 1f - pct;
		}

		private static long Build(HediffComp_Invisibility comp, bool forced, long structural)
		{
			var pawn = comp.parent.pawn;
			var mind = pawn.mindState;
			bool fadeIn = mind.lastBecameVisibleTick >= mind.lastBecameInvisibleTick;
			var anchor = fadeIn ? mind.lastBecameVisibleTick : mind.lastBecameInvisibleTick;

			var props = comp.Props;
			var duration = props.fadeDurationTicks;

			long bits = HasComp
				| structural
				| (forced ? ForcedVisible : 0L)
				| (props.visibleToPlayer ? VisibleToPlayer : 0L)
				| (fadeIn ? FadeInDirection : 0L);

			if (duration >= 0 && duration <= DurationMask)
				bits |= (long)duration << DurationShift;
			else
				bits |= DurationOverflow;

			bits |= (long)(uint)anchor << AnchorShift;
			return bits;
		}
	}
}
