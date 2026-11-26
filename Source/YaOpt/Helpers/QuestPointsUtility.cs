using System;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Supplies and memoises threat points for quest generation calls that pass zero points.
	/// </summary>
	/// <remarks>
	/// The fast quest selector may derive points from the target to build its weights, while the caller
	/// still hands the original zero to <see cref="QuestUtility.GenerateQuestAndMakeAvailable(QuestScriptDef, float)"/>.
	/// The last derived value is kept for 120 ticks, tied to the same incident target and invalidated on
	/// load. Explicit positive points are never replaced.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptQuestPointsNormalize"/>
	internal static class QuestPointsUtility
	{
		internal const float Epsilon = 0.01f;

		private const int RecentPointTicks = 120;

		[ThreadStatic]
		private static int _recentTick;

		[ThreadStatic]
		private static int _recentEpoch;

		[ThreadStatic]
		private static IIncidentTarget _recentTarget;

		[ThreadStatic]
		private static float _recentPoints;

		private static int _epoch;

		static QuestPointsUtility()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(AdvanceEpoch);
		}

		internal static float ComputeThreatPointsSafe(IIncidentTarget target)
		{
			if (target == null)
				return 0f;

			try
			{
				return StorytellerUtility.DefaultThreatPointsNow(target);
			}
			catch
			{
				return 0f;
			}
		}

		internal static void RecordAutoPoints(IIncidentTarget target, float points)
		{
			if (target == null || !(points > Epsilon))
				return;

			_recentTick = Find.TickManager?.TicksGame ?? 0;
			_recentEpoch = _epoch;
			_recentTarget = target;
			_recentPoints = points;
		}

		internal static bool TryGetRecentAutoPoints(IIncidentTarget target, out float points)
		{
			points = 0f;
			if (target == null || !ReferenceEquals(target, _recentTarget))
				return false;

			return TryReadRecent(out points);
		}

		internal static bool TryGetRecentAutoPoints(out float points)
		{
			points = 0f;
			return TryReadRecent(out points);
		}

		internal static bool TryGetTargetFromSlate(Slate slate, out IIncidentTarget target)
		{
			target = null;
			if (slate == null)
				return false;

			try
			{
				var map = slate.Get<Map>("map", null, false);
				if (map != null)
				{
					target = map;
					return true;
				}
			}
			catch
			{
				// Slate variables may hold a different type; try the next well-known key.
			}

			try
			{
				target = slate.Get<IIncidentTarget>("target", null, false);
				return target != null;
			}
			catch
			{
				return false;
			}
		}

		internal static bool TryGetSlatePoints(Slate slate, out float points)
		{
			points = 0f;
			return slate != null && slate.TryGet("points", out points, false);
		}

		internal static void SetSlatePoints(Slate slate, float points)
		{
			slate?.Set("points", points, false);
		}

		private static bool TryReadRecent(out float points)
		{
			points = 0f;
			if (_recentEpoch != _epoch || !(_recentPoints > Epsilon) || _recentTick == 0)
				return false;

			var now = Find.TickManager?.TicksGame ?? 0;
			if (now == 0 || now < _recentTick || now - _recentTick > RecentPointTicks)
				return false;

			points = _recentPoints;
			return true;
		}

		private static void AdvanceEpoch()
		{
			_epoch++;
		}
	}
}