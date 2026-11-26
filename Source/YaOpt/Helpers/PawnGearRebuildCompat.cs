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
	/// and locates foreign patches that need neutralising or explicit ordering.
	/// </summary>
	/// <remarks>
	/// Prefixes and transpilers can be bypassed by the rewrite, while postfixes still run. Unknown
	/// patches disable the option; HAR's pool patches are neutralised, Passer Race's generation context
	/// runs first, and known gear postfixes are preserved without runtime reflection. Top-up wear
	/// checks are memoized per Pawn only when a foreign wearable check is present.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_PawnGearNeutralizer"/>
	/// <seealso cref="PawnGearPools"/>
	internal static class PawnGearRebuildCompat
	{
		private const string SettingId = nameof(YaOpt.YaOptSettings.OptPawnGearRebuild);

		private const string HarPatchesType = "AlienRace.HarmonyPatches";

		private const string HarRestrictionsType = "AlienRace.RaceRestrictionSettings";

		internal const string PasserHarmonyId = "passerrace.final";

		private const string PasserApparelType = "PasserRace.PAS_ApparelCompatibility";

		private enum PatchRole
		{
			Transpiler,
			Prefix,
			Postfix,
			Finalizer
		}

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

		private static MethodInfo[] _passerPatchMethods;

		private static bool _requiresTopUpWearValidation;

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

		internal static bool RequiresTopUpWearValidation => _requiresTopUpWearValidation;

		internal static void Refresh()
		{
			_ready = false;
			_canSkipWear = false;
			_harPatchMethods = null;
			_passerPatchMethods = null;
			_requiresTopUpWearValidation = false;

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
				_passerPatchMethods = FindPasserGearPatches(apparelMethod);
				var reasons = new List<string>();
				CollectForeignConflicts(apparelMethod, reasons);
				CollectForeignConflicts(weaponMethod, reasons);
				if (reasons.Count > 0)
					ReportConflict(reasons);
			}

			_requiresTopUpWearValidation = PatchConflictGuard.HasForeignPatch(
				AccessTools.Method(typeof(ApparelProperties), nameof(ApparelProperties.PawnCanWear),
					new[] { typeof(Pawn), typeof(bool) }));

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
			// Current HAR builds expose the cleanup postfix under its original method name.
			if (result[3] == null)
				result[3] = FindHarPatch(weaponInfo.Postfixes, "TryGenerateWeaponForPostfix");

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

		private static MethodInfo[] FindPasserGearPatches(MethodBase apparelMethod)
		{
			var info = Harmony.GetPatchInfo(apparelMethod);
			if (info == null)
				return null;

			Patch prefix = null;
			Patch finalizer = null;
			for (var i = 0; i < info.Prefixes.Count; i++)
			{
				var patch = info.Prefixes[i];
				if (patch.owner == PasserHarmonyId
					&& patch.PatchMethod.DeclaringType?.FullName == PasserApparelType
					&& patch.PatchMethod.Name == "GenerationPrefix")
				{
					prefix = patch;
					break;
				}
			}

			if (prefix == null)
				return null;

			for (var i = 0; i < info.Finalizers.Count; i++)
			{
				var patch = info.Finalizers[i];
				if (patch.owner == PasserHarmonyId
					&& patch.PatchMethod.DeclaringType?.FullName == PasserApparelType
					&& patch.PatchMethod.Name == "GenerationFinalizer")
				{
					finalizer = patch;
					break;
				}
			}

			if (finalizer == null || prefix.priority != Priority.First || finalizer.priority != Priority.Last)
				return null;

			return new[] { prefix.PatchMethod, finalizer.PatchMethod };
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
				if (IsForeign(patch, harmonyId, earlyId, PatchRole.Transpiler))
					results.Add(patch.owner + " (transpiler) → " + targetName);
			}

			foreach (var patch in info.Prefixes)
			{
				if (IsForeign(patch, harmonyId, earlyId, PatchRole.Prefix))
					results.Add(patch.owner + " (prefix) → " + targetName);
			}

			foreach (var patch in info.Postfixes)
			{
				if (IsForeign(patch, harmonyId, earlyId, PatchRole.Postfix))
					results.Add(patch.owner + " (postfix) → " + targetName);
			}

			foreach (var patch in info.Finalizers)
			{
				if (IsForeign(patch, harmonyId, earlyId, PatchRole.Finalizer))
					results.Add(patch.owner + " (finalizer) → " + targetName);
			}
		}

		private static bool IsForeign(Patch patch, string harmonyId, string earlyId, PatchRole role)
		{
			if (patch.owner == harmonyId || patch.owner == earlyId)
				return false;

			var method = patch.PatchMethod;
			if (role == PatchRole.Transpiler && IsAlienRace(method)
				&& method.Name == HarWeaponTranspilerName)
			{
				return false;
			}

			if (_harPatchMethods != null)
			{
				for (var i = 0; i < _harPatchMethods.Length; i++)
				{
					if (_harPatchMethods[i] == method)
						return false;
				}
			}

			if (_passerPatchMethods != null)
			{
				if (role == PatchRole.Prefix && _passerPatchMethods[0] == method)
					return false;
				if (role == PatchRole.Finalizer && _passerPatchMethods[1] == method)
					return false;
			}

			return role != PatchRole.Postfix || !IsKnownGearPostfix(patch);
		}

		private static bool IsKnownGearPostfix(Patch patch)
		{
			var method = patch.PatchMethod;
			var type = method.DeclaringType?.FullName;
			switch (patch.owner)
			{
				case "wowgag.EvolvingEnemyStrongholds":
					return type == "EvolvingEnemyStrongholds.Patch_StripLeakedCommanderGear"
						&& method.Name == "Postfix";
				case "Fortified":
					return type == "Fortified.Patch_PawnWeaponGenerator_TryGenerateWeaponFor"
						&& method.Name == "Postfix";
				case "DMSSR":
					return type == "DMSSR.Bosses.Bullfrog.Patch_DendroFixedPrimaryWeapon"
						&& method.Name == "Postfix";
				case "OskarPotocki.VEF":
					return type == "VEF.Apparels.Patch_PawnApparelGenerator"
						&& method.Name == "GenerateStartingApparelFor_Postfix";
				default:
					return false;
			}
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