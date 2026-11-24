using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	[StaticConstructorOnStartup]
	internal static class ToggleTabCache
	{
		private static readonly Dictionary<MainButtonWorker, CacheEntry> tabCache =
			new Dictionary<MainButtonWorker, CacheEntry>();

		public static List<Type> ToggleTabTypes { get; } = new List<Type>();

		private class CacheEntry
		{
			public int Frame = -1;

			public bool Result;
		}

		static ToggleTabCache()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCache);
			TypeSearcher.RegisterSearchingType(typeof(MainButtonWorker_ToggleTab),
				MainButtonWorker_ToggleTab_Checker);
		}

		private static void ClearCache()
		{
			tabCache.Clear();
		}

		private static void MainButtonWorker_ToggleTab_Checker(Type type)
		{
			if (type == typeof(MainButtonWorker_ToggleTab))
				return;
			if (type == typeof(MainButtonWorker_ToggleMechTab))
				return;
			if (CompatibilityDefines.CachedIgnoredToggleTabCaching.Contains(type))
				return;

			// DeclaredOnly matters: an inherited getter belongs to the base type, and answering for it
			// here would cache the base type's own tab button (ToggleMechTab drives Visible through
			// Disabled, so a stale value makes the whole button disappear) without the base type ever
			// passing the exclusion above.
			var getter = type.GetProperty(nameof(MainButtonWorker.Disabled),
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetMethod;
			if (getter != null && getter.IsOverriden())
			{
				ToggleTabTypes.Add(type);
			}
		}

		/// <remarks>
		/// Keyed by the worker instance, not by its type: two defs may share one worker class and
		/// their answers are independent. Only the current frame is reused - the UI reads Disabled
		/// four to five times per event (the Visible reads included) and several events per frame,
		/// so the win is collapsing those reads, not delaying the answer.
		/// </remarks>
		public static bool TryGetResult(MainButtonWorker worker, out bool result)
		{
			result = false;
			if (!tabCache.TryGetValue(worker, out var entry) ||
				entry.Frame != UnityEngine.Time.frameCount)
				return false;
			result = entry.Result;
			return true;
		}

		public static void UpdateCache(MainButtonWorker worker, bool disabled)
		{
			if (!tabCache.TryGetValue(worker, out var entry))
			{
				entry = new CacheEntry();
				tabCache[worker] = entry;
			}
			entry.Frame = UnityEngine.Time.frameCount;
			entry.Result = disabled;
		}
	}
}
