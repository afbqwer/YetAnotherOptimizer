using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Detects whether the single-threaded pawn generation indexes can replace their target methods.
	/// </summary>
	/// <remarks>
	/// Every sub-path falls back independently. A foreign relation sampler, an unknown
	/// <see cref="PawnRelationWorker.GenerationChance"/> patch, or a patch on the deferred notification
	/// methods disables only that sub-path; the master option stays usable for the remaining paths.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGenerationIndex"/>
	/// <seealso cref="PawnRelationIndex"/>
	/// <seealso cref="GeneNotificationBatch"/>
	internal static class PawnGenerationIndexCompat
	{
		private const string RosalineUtilitiesType = "LoveyDoveySexWithRosaline.GenderUtilities";

		private static MethodInfo _rosalineMother;

		private static MethodInfo _rosalineFather;

		internal static bool MasterEnabled =>
			YaOptGlobal.Settings?.OptPawnGenerationIndex.Enabled == true;

		internal static bool RelationReady { get; private set; }

		internal static bool RosalineReady { get; private set; }

		internal static bool GeneBatchReady { get; private set; }

		internal static MethodInfo RosalineMother => _rosalineMother;

		internal static MethodInfo RosalineFather => _rosalineFather;

		internal static void Refresh()
		{
			RefreshRosalineMethods();
			RelationReady = CheckRelations();
			RosalineReady = CheckRosaline();
			GeneBatchReady = CheckGeneBatch();
		}

		/// <summary>
		/// Disables the relation sampler after a runtime failure so the next generation uses vanilla.
		/// </summary>
		internal static void DisableRelations(Exception exception)
		{
			if (!RelationReady)
				return;

			RelationReady = false;
			YaOptMod.Error("Pawn relation index disabled after runtime failure: " + exception);
		}

		private static void RefreshRosalineMethods()
		{
			_rosalineMother = null;
			_rosalineFather = null;

			var type = AccessTools.TypeByName(RosalineUtilitiesType);
			if (type == null)
				return;

			_rosalineMother = AccessTools.Method(type, "GetBirthMother");
			_rosalineFather = AccessTools.Method(type, "GetBirthFather");
			if (IsBirthParentMethod(_rosalineMother) && IsBirthParentMethod(_rosalineFather))
				return;

			_rosalineMother = null;
			_rosalineFather = null;
		}

		private static bool IsBirthParentMethod(MethodInfo method)
		{
			if (method == null || !method.IsStatic || method.ReturnType != typeof(Pawn))
				return false;

			var parameters = method.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType == typeof(Pawn);
		}

		private static bool HasRelationSignature(MethodInfo method)
		{
			if (method == null)
				return false;

			var parameters = method.GetParameters();
			return parameters.Length == 2
				&& parameters[0].ParameterType == typeof(Pawn)
				&& parameters[1].ParameterType.IsByRef
				&& parameters[1].ParameterType.GetElementType() == typeof(PawnGenerationRequest);
		}

		private static bool HasGenerateGenesSignature(MethodInfo method)
		{
			if (method == null)
				return false;

			var parameters = method.GetParameters();
			return parameters.Length == 3 && parameters[0].ParameterType == typeof(Pawn);
		}

		private static bool CheckRelations()
		{
			var target = AccessTools.Method(typeof(PawnGenerator), "GeneratePawnRelations");
			if (!HasRelationSignature(target))
				return false;

			var reasons = new List<string>();
			PatchConflictGuard.CollectForeignReplacementConflicts(target, reasons,
				compatiblePatch: IsKnownRelationPatch);
			PatchConflictGuard.CollectForeignPostfixConflicts(target, reasons,
				compatiblePatch: IsKnownRelationPostfix);
			CollectForeignFinalizers(target, reasons);
			var samples = AccessTools.Method(typeof(PawnGenerator), "GenerateSamples");
			PatchConflictGuard.CollectForeignConflicts(samples, includeAll: true, reasons);
			CollectForeignFinalizers(samples, reasons);
			CollectGenerationChanceConflicts(reasons);
			if (reasons.Count == 0)
				return true;

			if (MasterEnabled)
				YaOptMod.Warning("Pawn relation index falls back to vanilla: " + string.Join(", ", reasons));
			return false;
		}

		private static void CollectGenerationChanceConflicts(List<string> reasons)
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
				| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			var seen = new HashSet<MethodBase>();
			var reportedWorkers = new HashSet<Type>();
			var defs = DefDatabase<PawnRelationDef>.AllDefsListForReading;
			for (var i = 0; i < defs.Count; i++)
			{
				var worker = defs[i].Worker;
				if (worker == null)
					continue;

				for (var type = worker.GetType(); type != null
					&& typeof(PawnRelationWorker).IsAssignableFrom(type); type = type.BaseType)
				{
					var method = type.GetMethod(nameof(PawnRelationWorker.GenerationChance), flags);
					if (method == null)
						continue;
					if (!IsKnownWorkerType(type))
					{
						if (reportedWorkers.Add(type))
							reasons.Add("custom relation worker " + type.FullName);
						continue;
					}

					if (!seen.Add(method))
						continue;

					var info = Harmony.GetPatchInfo(method);
					if (info == null)
						continue;

					CollectConflicts(method, info.Prefixes, reasons);
					CollectConflicts(method, info.Postfixes, reasons);
					CollectConflicts(method, info.Transpilers, reasons);
					CollectConflicts(method, info.Finalizers, reasons);
				}
			}
		}

		private static bool IsKnownWorkerType(Type type)
		{
			if (type.Assembly == typeof(PawnRelationWorker).Assembly)
				return true;

			var assembly = type.Assembly.GetName().Name;
			return assembly == "AlienRace" || ContainsAny(assembly, "Rosaline", "LoveyDovey", "Euterpe");
		}

		private static void CollectConflicts(MethodBase target, IEnumerable<Patch> patches,
			List<string> reasons, bool allowKnownRelationPatch = true)
		{
			foreach (var patch in patches)
			{
				if (patch.owner == YaOptMod.Instance.Harmony.Id
					|| patch.owner == YaOptMod.Instance.EarlyHarmony.Id)
				{
					continue;
				}

				if (allowKnownRelationPatch && IsKnownRelationPatch(target, patch))
					continue;

				reasons.Add(patch.owner + " (" + patch.PatchMethod.Name + ") → " +
					target.DeclaringType?.Name + "." + target.Name);
			}
		}

		private static bool IsKnownRelationPatch(MethodBase target, Patch patch)
		{
			var method = patch.PatchMethod;
			var type = method.DeclaringType;
			if (type?.FullName == "AlienRace.HarmonyPatches")
			{
				if (method.Name == "GeneratePawnRelationsPrefix")
					return target.Name == "GeneratePawnRelations";

				return method.Name switch
				{
					"PawnRelationWorker_GenerationChance_Postfix" or
					"GenerationChanceChildPostfix" or
					"GenerationChanceExLoverPostfix" or
					"GenerationChanceExSpousePostfix" or
					"GenerationChanceFiancePostfix" or
					"GenerationChanceLoverPostfix" or
					"GenerationChanceParentPostfix" or
					"GenerationChanceSiblingPostfix" or
					"GenerationChanceSpousePostfix" => true,
					_ => false
				};
			}

			if (patch.owner == "rimworld.gguake.rpef"
				&& type?.FullName == "RPEF.HarmonyPatches"
				&& method.Name == "PawnRelationWorker_GenerationChance_Postfix")
			{
				return true;
			}

			return patch.owner == "SexWithEuterpe.LoveyDoveySexWithRosaline"
				&& type?.FullName == "LoveyDoveySexWithRosaline.PawnRelationWorker_Parent_Patch"
				&& method.Name == "GenerationChance_Prefix";
		}

		private static bool IsKnownRelationPostfix(MethodBase target, Patch patch)
		{
			return target.Name == "GeneratePawnRelations"
				&& patch.owner == "RER.FamilyNameFix"
				&& patch.PatchMethod.DeclaringType?.FullName == "FamilyNameFix.FamilyNameFix"
				&& patch.PatchMethod.Name == "Postfix";
		}

		private static bool ContainsAny(string value, params string[] parts)
		{
			if (string.IsNullOrEmpty(value))
				return false;

			for (var i = 0; i < parts.Length; i++)
			{
				if (value.IndexOf(parts[i], StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}

			return false;
		}

		private static bool CheckRosaline()
		{
			if (_rosalineMother == null || _rosalineFather == null)
				return false;

			var reasons = new List<string>();
			CollectForeignPatches(_rosalineMother, reasons);
			CollectForeignPatches(_rosalineFather, reasons);
			if (reasons.Count == 0)
				return true;

			if (MasterEnabled)
			{
				YaOptMod.Warning("Rosaline birth parent lookup falls back to its own method: " +
					string.Join(", ", reasons));
			}
			return false;
		}

		private static bool CheckGeneBatch()
		{
			var dirtyCache = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache));
			var notifyWorkTypes = AccessTools.Method(typeof(Pawn),
				nameof(Pawn.Notify_DisabledWorkTypesChanged));
			var graphicsDirty = AccessTools.Method(typeof(PawnRenderer),
				nameof(PawnRenderer.SetAllGraphicsDirty));
			var generateGenes = AccessTools.Method(typeof(PawnGenerator), "GenerateGenes");
			if (dirtyCache == null || notifyWorkTypes == null || graphicsDirty == null
				|| !HasGenerateGenesSignature(generateGenes)
				|| dirtyCache.GetParameters().Length != 0
				|| notifyWorkTypes.GetParameters().Length != 0
				|| graphicsDirty.GetParameters().Length != 0)
			{
				return false;
			}

			if (AccessTools.Method(typeof(HediffSet), "CacheMissingPartsCommonAncestors") == null
				|| AccessTools.Method(typeof(HediffSet), "CacheThoughtsNullified") == null
				|| AccessTools.Method(typeof(HediffSet), "CacheNeeds") == null)
			{
				return false;
			}

			var reasons = new List<string>();
			CollectForeignPatches(dirtyCache, reasons);
			CollectForeignPatches(notifyWorkTypes, reasons);
			CollectForeignPatches(graphicsDirty, reasons);
			if (reasons.Count == 0)
				return true;

			if (MasterEnabled)
			{
				YaOptMod.Warning("Gene notification batch falls back to per-gene notifications: " +
					string.Join(", ", reasons));
			}
			return false;
		}

		private static void CollectForeignFinalizers(MethodBase target, List<string> reasons)
		{
			if (target == null || YaOptMod.Instance == null)
				return;

			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			foreach (var patch in info.Finalizers)
			{
				if (patch.owner == YaOptMod.Instance.Harmony.Id
					|| patch.owner == YaOptMod.Instance.EarlyHarmony.Id)
				{
					continue;
				}

				reasons.Add(patch.owner + " (finalizer) → " +
					target.DeclaringType?.Name + "." + target.Name);
			}
		}

		private static void CollectForeignPatches(MethodBase target, List<string> reasons)
		{
			var info = Harmony.GetPatchInfo(target);
			if (info == null)
				return;

			CollectConflicts(target, info.Prefixes, reasons, allowKnownRelationPatch: false);
			CollectConflicts(target, info.Postfixes, reasons, allowKnownRelationPatch: false);
			CollectConflicts(target, info.Transpilers, reasons, allowKnownRelationPatch: false);
			CollectConflicts(target, info.Finalizers, reasons, allowKnownRelationPatch: false);
		}
	}
}