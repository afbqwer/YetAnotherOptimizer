using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using YaOpt.Defines;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches texture resolution for world-map layers that query the same path once per tile.
	/// </summary>
	/// <remarks>
	/// Only main-thread lookups are cached, destroyed textures are
	/// resolved again, and mod content reloads clear the cache. Any foreign Harmony patch on the
	/// target disables the cache because a path-only key cannot model caller-dependent results.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptWorldMapTextureCache"/>
	[HarmonyPatch(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get),
		new[] { typeof(string), typeof(bool) })]
	internal static class Verse_ContentFinder_TextureGet
	{
		private static readonly Dictionary<string, Texture2D> _cache =
			new Dictionary<string, Texture2D>(4096, StringComparer.Ordinal);

		// Missing textures are cached only for non-reporting lookups. A later reporting lookup
		// must still reach vanilla so its diagnostic is not silently swallowed.
		private static readonly HashSet<string> _missingCache =
			new HashSet<string>(StringComparer.Ordinal);

		private static bool _conflictChecked;
		private static bool _blocked;

		static Verse_ContentFinder_TextureGet()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(ClearCache);
		}

		private static bool Prepare()
		{
			return WorldMapOptimizationHelper.CanUse(YaOptGlobal.Settings.OptWorldMapTextureCache) &&
				   AccessTools.Method(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get),
					   new[] { typeof(string), typeof(bool) }) != null;
		}

		[HarmonyPriority(Priority.Last)]
		private static bool Prefix(string itemPath, bool reportFailure, ref Texture2D __result)
		{
			if (itemPath == null || _blocked || !UnityData.IsInMainThread)
				return true;

			if (!NoForeignPatch())
				return true;

			if (!_cache.TryGetValue(itemPath, out var cached))
			{
				if (!reportFailure && _missingCache.Contains(itemPath))
				{
					__result = null;
					return false;
				}

				return true;
			}

			// A destroyed Unity object compares equal to null and must be resolved again.
			if (!ReferenceEquals(cached, null) && cached == null)
			{
				_cache.Remove(itemPath);
				return true;
			}

			__result = cached;
			return false;
		}

		[HarmonyPriority(Priority.First)]
		private static void Postfix(string itemPath, Texture2D __result, bool reportFailure, bool __runOriginal)
		{
			if (!__runOriginal || itemPath == null || _blocked || !UnityData.IsInMainThread)
				return;

			if (NoForeignPatch())
			{
				if (__result != null)
					_cache[itemPath] = __result;
				else if (!reportFailure)
					_missingCache.Add(itemPath);
			}
		}

		internal static void ClearCache()
		{
			_cache.Clear();
			_missingCache.Clear();
		}

		private static bool NoForeignPatch()
		{
			if (_conflictChecked)
				return !_blocked;

			_conflictChecked = true;
			try
			{
				var target = AccessTools.Method(typeof(ContentFinder<Texture2D>),
					nameof(ContentFinder<Texture2D>.Get), new[] { typeof(string), typeof(bool) });
				var info = Harmony.GetPatchInfo(target);
				if (info == null)
					return true;

				var ownId = YaOptGlobal.Harmony.Id;
				foreach (var owner in info.Owners)
				{
					if (owner == ownId)
						continue;

					Block("another mod patches ContentFinder<Texture2D>.Get: " + owner);
					return false;
				}
			}
			catch (Exception ex)
			{
				Block("could not inspect Harmony patches: " + ex.Message);
			}

			return !_blocked;
		}

		private static void Block(string reason)
		{
			if (_blocked)
				return;

			_blocked = true;
			CompatibilityDefines.CachedBannedOptimizations.Add(nameof(YaOptSettings.OptWorldMapTextureCache));
			CompatibilityDefines.CachedBannedBy[nameof(YaOptSettings.OptWorldMapTextureCache)] = "其他纹理查找补丁";
			_cache.Clear();
			_missingCache.Clear();
			YaOptMod.Warning("World-map texture lookup cache disabled for this session: " + reason);
		}
	}
}
