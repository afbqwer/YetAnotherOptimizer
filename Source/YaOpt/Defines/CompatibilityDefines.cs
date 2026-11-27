using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Defines
{
	/// <summary>
	/// How an optimization became unavailable because of another mod.
	/// </summary>
	public enum ConflictKind
	{
		/// <summary>
		/// A known mod or XML conflict. The option is locked off and cannot be enabled.
		/// </summary>
		Ban,

		/// <summary>
		/// A foreign patch was detected on a method the optimization touches. The option defaults to
		/// off but the player may still enable it by hand.
		/// </summary>
		Patch
	}

	public static class CompatibilityDefines
	{
		/// <summary>
		/// Options a specific mod or XML compatibility def locked off.
		/// </summary>
		public static readonly HashSet<string> CachedBannedOptimizations = new HashSet<string>();

		public static readonly Dictionary<string, string> CachedBannedBy = new Dictionary<string, string>();

		/// <summary>
		/// Options a detected foreign patch disabled by default. Unlike a ban they can be enabled.
		/// </summary>
		public static readonly HashSet<string> CachedPatchConflicts = new HashSet<string>();

		/// <summary>
		/// Every foreign patch that conflicts with each option, so the settings can list them all.
		/// </summary>
		public static readonly Dictionary<string, List<string>> CachedPatchConflictsBy =
			new Dictionary<string, List<string>>();

		public static readonly HashSet<Type> CachedIgnoredToggleTabCaching = new HashSet<Type>();

		/// <summary>
		/// Locks an option off because of a known mod or XML conflict.
		/// </summary>
		public static void Ban(string settingId, string reason)
		{
			CachedPatchConflicts.Remove(settingId);
			CachedPatchConflictsBy.Remove(settingId);
			CachedBannedOptimizations.Add(settingId);
			CachedBannedBy[settingId] = reason;
		}

		/// <summary>
		/// Clears a hard ban after the external conflict disappears.
		/// </summary>
		/// <returns>True when the option was actually banned.</returns>
		public static bool Unban(string settingId)
		{
			var removed = CachedBannedOptimizations.Remove(settingId);
			CachedBannedBy.Remove(settingId);
			return removed;
		}

		/// <summary>
		/// Disables an option by default because a foreign patch was detected. The player can still
		/// enable it; the settings remember that decision.
		/// </summary>
		/// <returns>True when the conflict was recorded for the first time this session.</returns>
		public static bool PatchConflict(string settingId, string reason)
		{
			// A hard ban always wins: never downgrade it to an enableable default.
			if (CachedBannedOptimizations.Contains(settingId))
				return false;

			var added = CachedPatchConflicts.Add(settingId);
			AddConflictReason(settingId, reason);
			YaOptMod.Instance?.Settings?.ApplyPatchConflictDefault(settingId);
			return added;
		}

		/// <summary>
		/// Records every conflicting patch for the option so the settings can list them all.
		/// </summary>
		/// <returns>True when the option was recorded as conflicted for the first time this session.</returns>
		public static bool PatchConflict(string settingId, IReadOnlyList<string> reasons)
		{
			if (CachedBannedOptimizations.Contains(settingId))
				return false;
			if (reasons == null || reasons.Count == 0)
				return false;

			var added = CachedPatchConflicts.Add(settingId);
			for (var i = 0; i < reasons.Count; i++)
				AddConflictReason(settingId, reasons[i]);
			YaOptMod.Instance?.Settings?.ApplyPatchConflictDefault(settingId);
			return added;
		}

		private static void AddConflictReason(string settingId, string reason)
		{
			if (string.IsNullOrEmpty(reason))
				return;

			if (!CachedPatchConflictsBy.TryGetValue(settingId, out var list))
			{
				list = new List<string>();
				CachedPatchConflictsBy[settingId] = list;
			}

			if (!list.Contains(reason))
				list.Add(reason);
		}

		/// <summary>
		/// Returns whether an option has an active conflict. Allocation free; used by the per-frame
		/// label marker.
		/// </summary>
		public static bool TryGetConflict(string settingId, out ConflictKind kind)
		{
			if (CachedBannedOptimizations.Contains(settingId))
			{
				kind = ConflictKind.Ban;
				return true;
			}

			if (CachedPatchConflicts.Contains(settingId))
			{
				kind = ConflictKind.Patch;
				return true;
			}

			kind = ConflictKind.Ban;
			return false;
		}

		/// <summary>
		/// Returns the active conflict for an option together with every recorded reason.
		/// </summary>
		public static bool TryGetConflict(string settingId, out ConflictKind kind,
			out IReadOnlyList<string> reasons)
		{
			if (CachedBannedOptimizations.Contains(settingId))
			{
				kind = ConflictKind.Ban;
				var reason = CachedBannedBy.TryGetValue(settingId, out var stored) ? stored : string.Empty;
				reasons = new[] { reason };
				return true;
			}

			if (CachedPatchConflicts.Contains(settingId))
			{
				kind = ConflictKind.Patch;
				reasons = CachedPatchConflictsBy.TryGetValue(settingId, out var list) && list.Count > 0
					? list
					: Array.Empty<string>();
				return true;
			}

			kind = ConflictKind.Ban;
			reasons = Array.Empty<string>();
			return false;
		}

		public static void Load()
		{
			foreach (var def in DefDatabase<CompatibilityDef>.AllDefs)
			{
				var owner = def.modContentPack.Name;

				try
				{
					def.Cache();
				}
				catch (Exception e)
				{
					YaOptMod.Error($"Error when parsing CompatibilityDef {def.defName} from {owner}. " +
								   $"{e.ToStringSafe()}");
				}
			}
		}
	}
}