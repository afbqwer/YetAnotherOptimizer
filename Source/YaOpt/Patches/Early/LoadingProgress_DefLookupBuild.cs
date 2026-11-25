using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Early
{
	/// <summary>
	/// Loading Progress replaces the static-constructor phase: it runs every constructor itself
	/// and only calls <c>CallAll</c> afterwards. Its enumerator is the first point after the
	/// deferred startup actions (which may still modify defs) and before the first constructor,
	/// so the index is built there when the mod is present.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptDefLookupCache" />
	/// <seealso cref="YaOptSettings.OptBiomeAnimalIndex" />
	[EarlyPatch]
	[ManualPatch]
	internal static class LoadingProgress_DefLookupBuild
	{
		private const string ReplacementType =
			"ilyvion.LoadingProgress.StaticConstructorOnStartupUtilityReplacement";

		internal static bool Patch(Harmony harmony)
		{
			if (!DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptDefLookupCache) &&
				!DefLookupCacheHelper.CanUse(YaOptGlobal.Settings.OptBiomeAnimalIndex))
			{
				return true;
			}

			var replacement = AccessTools.TypeByName(ReplacementType);
			if (replacement == null)
				return true;

			// Prefixing the iterator method runs when the enumerator is created, inside Interject(),
			// after the queued startup actions and before the constructor loop starts.
			var patched = TryPatch(harmony, AccessTools.Method(replacement, "CallAllAndRest"));

			// The iterator method is tiny and may be inlined into its caller; MoveNext is called for
			// every constructor, long before the first one runs, and is not inlined.
			var iterator = FindIterator(replacement);
			patched |= TryPatch(harmony, iterator == null ? null : AccessTools.Method(iterator, "MoveNext"));

			if (!patched)
			{
				YaOptMod.Warning(
					"Loading Progress detected but its constructor loop could not be patched; the index " +
					"will be built at CallAll instead.");
			}

			return true;
		}

		private static Type FindIterator(Type replacement)
		{
			var nested = replacement.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
			for (var i = 0; i < nested.Length; i++)
			{
				var type = nested[i];
				if (type.Name.StartsWith("<CallAllAndRest>d__", StringComparison.Ordinal) &&
					typeof(IEnumerator).IsAssignableFrom(type))
				{
					return type;
				}
			}

			return null;
		}

		private static bool TryPatch(Harmony harmony, MethodInfo method)
		{
			if (method == null)
				return false;

			try
			{
				harmony.Patch(method,
					prefix: new HarmonyMethod(typeof(LoadingProgress_DefLookupBuild), nameof(Prefix)));
				return true;
			}
			catch (Exception exception)
			{
				YaOptMod.Warning($"Could not patch {method}, trying the next hook:\n{exception}");
				return false;
			}
		}

		private static void Prefix()
		{
			if (!PlayDataLoader.Loaded)
				return;

			DefLookupCacheHelper.BuildAll("Loading Progress constructor loop");
		}
	}
}