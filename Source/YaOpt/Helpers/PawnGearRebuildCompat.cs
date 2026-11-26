using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Defines;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Decides whether the constructive pawn gear rewrite can replace the vanilla generation methods,
	/// and locates the Humanoid Alien Races patch methods that have to be neutralised first.
	/// </summary>
	/// <remarks>
	/// Replacing the two generation methods bypasses every other patch on them, so any foreign patch
	/// disables the option unless it belongs to the known HAR gear patches, which are neutralised. HAR's
	/// patch surface is all-or-nothing: if any of its four gear patch methods is missing, the pools it
	/// builds and restores would get out of sync and corrupt the shared pair lists, so the option is
	/// disabled and vanilla plus HAR run unchanged. The check runs on every patch attempt before patches
	/// are applied, and a runtime failure also falls back for the rest of the session.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_PawnGearNeutralizer"/>
	/// <seealso cref="PawnGearPools"/>
	internal static class PawnGearRebuildCompat
	{
		private const string SettingId = nameof(YaOpt.YaOptSettings.OptPawnGearRebuild);

		private const string HarPatchesType = "AlienRace.HarmonyPatches";

		private const string HarRestrictionsType = "AlienRace.RaceRestrictionSettings";

		/// <summary>
		/// HAR's only other patch on the replaced methods. It injects pool restore calls into the
		/// original body, which the replacement never runs, so it does not need to be neutralised.
		/// </summary>
		private const string HarWeaponTranspilerName = "TryGenerateWeaponForTranspiler";

		private static readonly string[] HarGearPatchNames =
		{
			"GenerateStartingApparelForPrefix",
			"GenerateStartingApparelForPostfix",
			"TryGenerateWeaponForPrefix",
			"TryGenerateWeaponForCleanup"
		};

		private static bool _ready;

		private static bool _canSkipWear;

		private static MethodInfo[] _harPatchMethods;

		/// <summary>
		/// True when the rewrite is enabled and all compatibility checks passed for this patch cycle.
		/// </summary>
		internal static bool Ready => _ready;

		/// <summary>
		/// True when the rewrite should run right now.
		/// </summary>
		internal static bool Active => _ready && YaOptGlobal.Settings?.OptPawnGearRebuild.Enabled == true;

		/// <summary>
		/// True when the rewrite may wear the constructed set directly instead of calling the vanilla
		/// wear path.
		/// </summary>
		internal static bool CanSkipWear => _canSkipWear;

		internal static MethodInfo[] HarPatchMethods => _harPatchMethods;

		internal static void Refresh()
		{
			_ready = false;
			_canSkipWear = false;
			_harPatchMethods = null;

			var option = YaOptGlobal.Settings?.OptPawnGearRebuild;
			var apparelMethod = AccessTools.Method(typeof(PawnApparelGenerator),
				nameof(PawnApparelGenerator.GenerateStartingApparelFor));
			var weaponMethod = AccessTools.Method(typeof(PawnWeaponGenerator),
				nameof(PawnWeaponGenerator.TryGenerateWeaponFor));

			// Detect even when the option is off so the settings can show the reason and apply the
			// default. A concrete HAR incompatibility is a hard ban and leaves nothing to run.
			if (apparelMethod == null || weaponMethod == null)
			{
				ReportConflict("找不到装备生成方法");
			}
			else if (!CheckHar(apparelMethod, weaponMethod))
			{
				PawnGearPools.Configure(null, null);
				return;
			}
			else
			{
				var reasons = new List<string>();
				CollectForeignConflicts(apparelMethod, reasons);
				CollectForeignConflicts(weaponMethod, reasons);
				if (reasons.Count > 0)
					ReportConflict(reasons);
			}

			if (option == null || !option.Enabled || apparelMethod == null || weaponMethod == null)
			{
				PawnGearPools.Configure(null, null);
				return;
			}

			_ready = true;
			_canSkipWear = CanReplaceWear();
			if (!_canSkipWear)
				YaOptMod.Debug("OptPawnGearRebuild: vanilla wear path kept, a foreign patch touches it or HasPartsToWear");
		}

		/// <summary>
		/// Checks the Humanoid Alien Races patch surface and neutralises its gear patches. Returns
		/// false when HAR is present but incomplete, which disables the rewrite entirely.
		/// </summary>
		private static bool CheckHar(MethodBase apparelMethod, MethodBase weaponMethod)
		{
			var harPatches = AccessTools.TypeByName(HarPatchesType);
			var harRestrictions = AccessTools.TypeByName(HarRestrictionsType);
			if (harPatches == null && harRestrictions == null)
			{
				PawnGearPools.Configure(null, null);
				return true;
			}

			if (harPatches == null || harRestrictions == null)
			{
				HardBan("HAR 类型不完整");
				return false;
			}

			var canWear = AccessTools.Method(harRestrictions, "CanWear",
				new[] { typeof(ThingDef), typeof(ThingDef) });
			var canEquip = AccessTools.Method(harRestrictions, "CanEquip",
				new[] { typeof(ThingDef), typeof(ThingDef) });
			if (canWear == null || canEquip == null)
			{
				HardBan("HAR 种族限制方法缺失");
				return false;
			}

			var canWearDelegate = AccessTools.MethodDelegate<Func<ThingDef, ThingDef, bool>>(canWear);
			var canEquipDelegate = AccessTools.MethodDelegate<Func<ThingDef, ThingDef, bool>>(canEquip);
			if (canWearDelegate == null || canEquipDelegate == null)
			{
				HardBan("HAR 种族限制方法无法绑定");
				return false;
			}

			PawnGearPools.Configure(canWearDelegate, canEquipDelegate);

			_harPatchMethods = FindHarGearPatches(apparelMethod, weaponMethod);
			if (_harPatchMethods == null)
			{
				HardBan("HAR 装备补丁面不完整");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Disables the rewrite after a runtime failure and logs the reason once.
		/// </summary>
		internal static void Fail(Exception exception)
		{
			if (!_ready)
				return;

			_ready = false;
			_canSkipWear = false;
			YaOptMod.Error("OptPawnGearRebuild disabled after runtime failure: " + exception);
		}

		/// <summary>
		/// The rewrite wears generated apparel through <c>ThingOwner&lt;Apparel&gt;.TryAdd</c> and answers
		/// body part checks itself. Any foreign patch on the skipped members would be silently bypassed,
		/// so the vanilla path is kept in that case instead of banning the whole option.
		/// </summary>
		private static bool CanReplaceWear()
		{
			if (PatchConflictGuard.HasForeignPatch(
					AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear)))
				|| PatchConflictGuard.HasForeignPatch(
					AccessTools.Method(typeof(ApparelUtility), nameof(ApparelUtility.HasPartsToWear))))
			{
				return false;
			}

			var giveToPawn = AccessTools.Method(typeof(PawnApparelGenerator.PossibleApparelSet),
				nameof(PawnApparelGenerator.PossibleApparelSet.GiveToPawn));
			return !PatchConflictGuard.HasForeignPatch(giveToPawn);
		}

		private static MethodInfo[] FindHarGearPatches(MethodBase apparelMethod, MethodBase weaponMethod)
		{
			var apparelInfo = Harmony.GetPatchInfo(apparelMethod);
			var weaponInfo = Harmony.GetPatchInfo(weaponMethod);
			if (apparelInfo == null || weaponInfo == null)
				return null;

			var result = new MethodInfo[HarGearPatchNames.Length];
			result[0] = FindHarPatch(apparelInfo.Prefixes, HarGearPatchNames[0]);
			result[1] = FindHarPatch(apparelInfo.Postfixes, HarGearPatchNames[1]);
			result[2] = FindHarPatch(weaponInfo.Prefixes, HarGearPatchNames[2]);
			result[3] = FindHarPatch(weaponInfo.Postfixes, HarGearPatchNames[3]);

			for (var i = 0; i < result.Length; i++)
			{
				if (result[i] == null)
					return null;
			}

			return result;
		}

		private static MethodInfo FindHarPatch(IEnumerable<Patch> patches, string methodName)
		{
			foreach (var patch in patches)
			{
				var method = patch.PatchMethod;
				if (method.Name == methodName && IsAlienRace(method))
					return method;
			}

			return null;
		}

		private static bool IsAlienRace(MethodInfo method)
		{
			return method.DeclaringType?.Assembly.GetName().Name == "AlienRace";
		}

		private static void CollectForeignConflicts(MethodBase target, List<string> results)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			var harmonyId = YaOptMod.Instance.Harmony.Id;
			var earlyId = YaOptMod.Instance.EarlyHarmony.Id;
			var targetName = target.DeclaringType?.Name + "." + target.Name;

			foreach (var patch in info.Transpilers)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					results.Add(patch.owner + " (transpiler) → " + targetName);
			}

			foreach (var patch in info.Prefixes)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					results.Add(patch.owner + " (prefix) → " + targetName);
			}

			foreach (var patch in info.Postfixes)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					results.Add(patch.owner + " (postfix) → " + targetName);
			}
		}

		private static bool IsForeign(Patch patch, string harmonyId, string earlyId)
		{
			if (patch.owner == harmonyId || patch.owner == earlyId)
				return false;

			var method = patch.PatchMethod;
			if (IsAlienRace(method) && method.Name == HarWeaponTranspilerName)
				return false;

			if (_harPatchMethods != null)
			{
				for (var i = 0; i < _harPatchMethods.Length; i++)
				{
					if (_harPatchMethods[i] == method)
						return false;
				}
			}

			return true;
		}

		/// <summary>
		/// A foreign patch (or missing vanilla method) disables the rewrite by default. The player may
		/// still enable it, in which case the rewrite runs over the other patch.
		/// </summary>
		private static void ReportConflict(string reason)
		{
			CompatibilityDefines.PatchConflict(SettingId, reason);
			YaOptMod.Warning("OptPawnGearRebuild disabled by default: " + reason);
		}

		private static void ReportConflict(IReadOnlyList<string> reasons)
		{
			CompatibilityDefines.PatchConflict(SettingId, reasons);
			YaOptMod.Warning("OptPawnGearRebuild disabled by default: " + string.Join(", ", reasons));
		}

		/// <summary>
		/// A concrete Humanoid Alien Races incompatibility that makes the rewrite unsafe.
		/// </summary>
		private static void HardBan(string reason)
		{
			_ready = false;
			_harPatchMethods = null;
			CompatibilityDefines.Ban(SettingId, reason);
			YaOptMod.Warning("OptPawnGearRebuild banned: " + reason);
		}
	}
}