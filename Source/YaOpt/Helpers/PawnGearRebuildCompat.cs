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

		private static MethodInfo[] _harPatchMethods;

		/// <summary>
		/// True when the rewrite is enabled and all compatibility checks passed for this patch cycle.
		/// </summary>
		internal static bool Ready => _ready;

		/// <summary>
		/// True when the rewrite should run right now.
		/// </summary>
		internal static bool Active => _ready && YaOptGlobal.Settings?.OptPawnGearRebuild.Enabled == true;

		internal static MethodInfo[] HarPatchMethods => _harPatchMethods;

		internal static void Refresh()
		{
			_ready = false;
			_harPatchMethods = null;

			var option = YaOptGlobal.Settings?.OptPawnGearRebuild;
			if (option == null || !option.Enabled)
			{
				PawnGearPools.Configure(null, null);
				return;
			}

			var apparelMethod = AccessTools.Method(typeof(PawnApparelGenerator),
				nameof(PawnApparelGenerator.GenerateStartingApparelFor));
			var weaponMethod = AccessTools.Method(typeof(PawnWeaponGenerator),
				nameof(PawnWeaponGenerator.TryGenerateWeaponFor));
			if (apparelMethod == null || weaponMethod == null)
			{
				Ban("找不到装备生成方法");
				return;
			}

			var harPatches = AccessTools.TypeByName(HarPatchesType);
			var harRestrictions = AccessTools.TypeByName(HarRestrictionsType);
			if (harPatches != null || harRestrictions != null)
			{
				if (harPatches == null || harRestrictions == null)
				{
					Ban("HAR 类型不完整");
					return;
				}

				var canWear = AccessTools.Method(harRestrictions, "CanWear",
					new[] { typeof(ThingDef), typeof(ThingDef) });
				var canEquip = AccessTools.Method(harRestrictions, "CanEquip",
					new[] { typeof(ThingDef), typeof(ThingDef) });
				if (canWear == null || canEquip == null)
				{
					Ban("HAR 种族限制方法缺失");
					return;
				}

				var canWearDelegate = AccessTools.MethodDelegate<Func<ThingDef, ThingDef, bool>>(canWear);
				var canEquipDelegate = AccessTools.MethodDelegate<Func<ThingDef, ThingDef, bool>>(canEquip);
				if (canWearDelegate == null || canEquipDelegate == null)
				{
					Ban("HAR 种族限制方法无法绑定");
					return;
				}

				PawnGearPools.Configure(canWearDelegate, canEquipDelegate);

				_harPatchMethods = FindHarGearPatches(apparelMethod, weaponMethod);
				if (_harPatchMethods == null)
				{
					Ban("HAR 装备补丁面不完整");
					return;
				}
			}
			else
			{
				PawnGearPools.Configure(null, null);
			}

			var conflict = FindForeignConflict(apparelMethod) ?? FindForeignConflict(weaponMethod);
			if (conflict != null)
			{
				Ban("其他 Mod 补丁：" + conflict);
				return;
			}

			_ready = true;
		}

		/// <summary>
		/// Disables the rewrite after a runtime failure and logs the reason once.
		/// </summary>
		internal static void Fail(Exception exception)
		{
			if (!_ready)
				return;

			_ready = false;
			YaOptMod.Error("OptPawnGearRebuild disabled after runtime failure: " + exception);
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

		private static string FindForeignConflict(MethodBase target)
		{
			if (target == null || YaOptMod.Instance == null)
				return null;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return null;

			var harmonyId = YaOptMod.Instance.Harmony.Id;
			var earlyId = YaOptMod.Instance.EarlyHarmony.Id;

			foreach (var patch in info.Transpilers)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					return patch.owner + " (transpiler)";
			}

			foreach (var patch in info.Prefixes)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					return patch.owner + " (prefix)";
			}

			foreach (var patch in info.Postfixes)
			{
				if (IsForeign(patch, harmonyId, earlyId))
					return patch.owner + " (postfix)";
			}

			return null;
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

		private static void Ban(string reason)
		{
			_ready = false;
			_harPatchMethods = null;
			if (CompatibilityDefines.CachedBannedOptimizations.Add(SettingId))
				CompatibilityDefines.CachedBannedBy[SettingId] = reason;
			YaOptMod.Warning("OptPawnGearRebuild disabled: " + reason);
		}
	}
}