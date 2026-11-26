using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Schedules the vanilla alert scan by the measured cost of each alert's recalculation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla <c>AlertsReadoutUpdate</c> recalculates one stride of alerts per frame, so every alert
	/// is recomputed every 24 frames no matter how expensive its <c>GetReport()</c> is. This helper
	/// is called from the main scan loop instead: cheap alerts keep the vanilla cadence, while an
	/// alert whose measured cost exceeds the threshold is offered again only after a
	/// cost-proportional interval. Critical alerts keep the vanilla schedule, and the quest, precept,
	/// scenario and action-delay blocks are not routed through here at all.
	/// </para>
	/// <para>
	/// Costs are learned per alert instance for the current <see cref="AlertsReadout"/> only. The
	/// table resets when the readout is replaced, so a new game starts with a short exploration
	/// period instead of trusting numbers from another save or mod list.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptAlertThrottle"/>
	/// <seealso cref="Patches.RimWorld_AlertsReadout_AlertsReadoutUpdate"/>
	internal static class AlertThrottleHelper
	{
		/// <summary>
		/// Runs per alert at the vanilla cadence before cost-based scheduling starts.
		/// </summary>
		private const int WarmupSamples = 10;

		/// <summary>
		/// Vanilla offers each alert every 24 frames; intervals below that cannot be honored.
		/// </summary>
		private const double MinIntervalSeconds = 0.4;

		/// <summary>
		/// Seconds of spacing per measured millisecond of alert work.
		/// </summary>
		private const double CostToIntervalSecondsPerMs = 1.5;

		private const double HighPriorityMaxIntervalSeconds = 2.0;

		private const double MediumPriorityMaxIntervalSeconds = 5.0;

		/// <summary>
		/// Re-check quickly right after an alert changes state, so the icon is confirmed promptly.
		/// </summary>
		private const double StateChangeRecheckSeconds = 0.5;

		private const double SpikeClampFactor = 4.0;

		private const double SpikeClampFloorMs = 5.0;

		private const double RiseAlpha = 0.1;

		private const double FallAlpha = 0.5;

		private static readonly Dictionary<Alert, AlertThrottleState> _states =
			new Dictionary<Alert, AlertThrottleState>();

		private static AlertsReadout _readout;

		private sealed class AlertThrottleState
		{
			public double CostMs;

			public double NextDueSeconds;

			public int Samples;

			public bool LastActive;

			public AlertThrottleState(bool lastActive)
			{
				LastActive = lastActive;
			}
		}

		/// <summary>
		/// Drop-in replacement for the scan loop's <c>CheckAddOrRemoveAlert</c> call.
		/// </summary>
		/// <remarks>
		/// Everything that is not the fixed alert scan falls through to vanilla: a null alert (vanilla
		/// reports it and removes it), a forced removal, the option being off, a non-main thread call,
		/// and critical alerts. The alert's own <c>GetReport()</c> still runs through the original
		/// method, so other Harmony patches on it keep working.
		/// </remarks>
		public static void Run(AlertsReadout readout, Alert alert, bool forceRemove)
		{
			if (alert == null || forceRemove ||
				YaOptGlobal.Settings?.OptAlertThrottle.Enabled != true ||
				!YaOptGlobal.IsInMainThread)
			{
				readout.CheckAddOrRemoveAlert(alert, forceRemove);
				return;
			}

			if (alert.Priority == AlertPriority.Critical)
			{
				readout.CheckAddOrRemoveAlert(alert, false);
				return;
			}

			if (!ReferenceEquals(readout, _readout))
			{
				_readout = readout;
				_states.Clear();
			}

			if (!_states.TryGetValue(alert, out var state))
			{
				state = new AlertThrottleState(alert.Active);
				_states[alert] = state;
			}

			var now = NowSeconds();
			if (state.Samples >= WarmupSamples && now < state.NextDueSeconds)
				return;

			var start = Stopwatch.GetTimestamp();
			readout.CheckAddOrRemoveAlert(alert, false);
			Record(state, alert, start, now);
		}

		private static void Record(AlertThrottleState state, Alert alert, long start, double now)
		{
			var elapsedMs = (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency);
			if (elapsedMs < 0)
				elapsedMs = 0;

			state.Samples++;
			if (state.CostMs <= 0)
			{
				state.CostMs = elapsedMs;
			}
			else
			{
				var clamp = state.CostMs * SpikeClampFactor;
				if (clamp < SpikeClampFloorMs)
					clamp = SpikeClampFloorMs;
				if (elapsedMs > clamp)
					elapsedMs = clamp;

				// Fall fast and rise slowly: one GC pause must not pin an otherwise cheap alert at the cap.
				var alpha = elapsedMs < state.CostMs ? FallAlpha : RiseAlpha;
				state.CostMs += (elapsedMs - state.CostMs) * alpha;
			}

			var interval = IntervalFor(alert.Priority, state.CostMs);
			var active = alert.Active;
			if (active != state.LastActive)
			{
				state.LastActive = active;
				if (interval > StateChangeRecheckSeconds)
					interval = StateChangeRecheckSeconds;
			}

			state.NextDueSeconds = now + interval;
		}

		private static double IntervalFor(AlertPriority priority, double costMs)
		{
			var interval = costMs * CostToIntervalSecondsPerMs;
			var max = priority == AlertPriority.High
				? HighPriorityMaxIntervalSeconds
				: MediumPriorityMaxIntervalSeconds;
			if (interval > max)
				interval = max;
			if (interval < MinIntervalSeconds)
				interval = MinIntervalSeconds;
			return interval;
		}

		private static double NowSeconds()
		{
			return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
		}
	}
}