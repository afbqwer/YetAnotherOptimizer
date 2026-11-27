using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Jobs;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Starts dynamic culling and pawn render preparation before the map mesh is drawn so worker
	/// threads can process the work while the main thread renders terrain.
	/// </summary>
	/// <remarks>
	/// The four patches belong together: <see cref="DynamicDrawManager.DrawDynamicThings"/> is rewritten
	/// to take the early-prepared details, the two job producers stop completing their handles, and
	/// <see cref="Verse.Map.MapUpdate"/> starts the jobs before the terrain pass. If any shape check
	/// fails the whole set is rolled back so the deferred completion never runs without a consumer.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptEarlyRenderPrepare"/>
	/// <seealso cref="ParallelPreDrawHelper"/>
	[ManualPatch]
	internal static class EarlyRenderPrepare
	{
		private const string DrawVisibleMarker = "Draw Visible";

		private static bool _lastMatched;

		private static MethodInfo _drawTranspiler;

		private static MethodInfo _cullTranspiler;

		private static MethodInfo _preDrawTranspiler;

		static void Patch(Harmony harmony)
		{
			ParallelPreDrawHelper.EarlyRenderPrepareActive = false;

			if (!YaOptGlobal.Settings.OptEarlyRenderPrepare.Enabled)
				return;

			var drawDynamic = AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings));
			var computeCulled = AccessTools.Method(typeof(DynamicDrawManager), "ComputeCulledThings");
			var preDrawVisible = AccessTools.Method(typeof(DynamicDrawManager), "PreDrawVisibleThings");
			var mapUpdate = AccessTools.Method(typeof(Map), nameof(Map.MapUpdate));
			if (drawDynamic == null || computeCulled == null || preDrawVisible == null || mapUpdate == null)
			{
				YaOptMod.Warning("Early Render Preparation could not find its vanilla targets; leaving vanilla rendering in place.");
				return;
			}

			_drawTranspiler = AccessTools.Method(typeof(EarlyRenderPrepare), nameof(SkipVanillaPreparation));
			_cullTranspiler = AccessTools.Method(typeof(EarlyRenderPrepare), nameof(DeferCullingJobCompletion));
			_preDrawTranspiler = AccessTools.Method(typeof(EarlyRenderPrepare), nameof(DeferPreDrawJobCompletion));
			var mapTranspiler = AccessTools.Method(typeof(EarlyRenderPrepare), nameof(InsertEarlyJobStarts));

			var drawApplied = TryApply(harmony, drawDynamic, _drawTranspiler);
			if (!drawApplied)
			{
				YaOptMod.Warning("Early Render Preparation did not match DrawDynamicThings; leaving vanilla rendering in place.");
				return;
			}

			var cullApplied = TryApply(harmony, computeCulled, _cullTranspiler);
			var preDrawApplied = cullApplied && TryApply(harmony, preDrawVisible, _preDrawTranspiler);
			var mapApplied = preDrawApplied && TryApply(harmony, mapUpdate, mapTranspiler);

			if (!mapApplied)
			{
				if (preDrawApplied)
					harmony.Unpatch(preDrawVisible, _preDrawTranspiler);
				if (cullApplied)
					harmony.Unpatch(computeCulled, _cullTranspiler);
				harmony.Unpatch(drawDynamic, _drawTranspiler);
				YaOptMod.Warning("Early Render Preparation could not patch every target; leaving vanilla rendering in place.");
				return;
			}

			ParallelPreDrawHelper.EarlyRenderPrepareActive = true;
			YaOptMod.Debug("Early Render Preparation transpilers installed.");
		}

		private static bool HasCall(List<CodeInstruction> codes, MethodInfo method)
		{
			foreach (var code in codes)
			{
				if (code.opcode == OpCodes.Call && code.operand is MethodInfo called && called == method)
					return true;
			}

			return false;
		}

		private static bool TryApply(Harmony harmony, MethodBase target, MethodInfo transpiler)
		{
			_lastMatched = false;
			try
			{
				harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
			}
			catch (Exception ex)
			{
				YaOptMod.Error(ex.ToString());
				return false;
			}
			return _lastMatched;
		}

		/// <summary>
		/// Replaces the array allocation, cull call, ensure-initialized loop and pre-draw call with a
		/// single call that returns the details prepared before the terrain pass.
		/// </summary>
		private static IEnumerable<CodeInstruction> SkipVanillaPreparation(IEnumerable<CodeInstruction> instructions)
		{
			_lastMatched = false;
			var codes = new List<CodeInstruction>(instructions);

			var takeDetails = AccessTools.Method(typeof(ParallelPreDrawHelper),
				nameof(ParallelPreDrawHelper.TakeOrPrepareDetails), new[] { typeof(DynamicDrawManager) });
			if (takeDetails == null)
				return codes;

			if (HasCall(codes, takeDetails))
			{
				_lastMatched = true;
				return codes;
			}

			var flagStore = -1;
			var drawVisible = -1;
			for (var i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Stloc_0)
				{
					if (flagStore >= 0)
						return codes;
					flagStore = i;
				}
				else if (flagStore >= 0 && codes[i].opcode == OpCodes.Ldstr &&
						 DrawVisibleMarker.Equals(codes[i].operand))
				{
					drawVisible = i;
					break;
				}
			}

			if (flagStore < 0 || drawVisible <= flagStore + 1)
				return codes;

			var result = new List<CodeInstruction>(codes.Count + 4);
			for (var i = 0; i <= flagStore; i++)
				result.Add(codes[i]);

			result.Add(new CodeInstruction(OpCodes.Ldarg_0));
			result.Add(new CodeInstruction(OpCodes.Call, takeDetails));
			result.Add(new CodeInstruction(OpCodes.Stloc_1));

			for (var i = drawVisible; i < codes.Count; i++)
				result.Add(codes[i]);

			_lastMatched = true;
			return result;
		}

		private static IEnumerable<CodeInstruction> DeferCullingJobCompletion(IEnumerable<CodeInstruction> instructions)
		{
			return DeferJobCompletion(instructions, nameof(ParallelPreDrawHelper.CompleteCullingJob));
		}

		private static IEnumerable<CodeInstruction> DeferPreDrawJobCompletion(IEnumerable<CodeInstruction> instructions)
		{
			return DeferJobCompletion(instructions, nameof(ParallelPreDrawHelper.CompletePreDrawJob));
		}

		private static IEnumerable<CodeInstruction> DeferJobCompletion(
			IEnumerable<CodeInstruction> instructions, string replacementName)
		{
			_lastMatched = false;
			var codes = new List<CodeInstruction>(instructions);

			var complete = AccessTools.Method(typeof(JobHandle), nameof(JobHandle.Complete));
			var replacement = AccessTools.Method(typeof(ParallelPreDrawHelper), replacementName,
				new[] { typeof(JobHandle).MakeByRefType() });
			if (complete == null || replacement == null)
				return codes;

			if (HasCall(codes, replacement))
			{
				_lastMatched = true;
				return codes;
			}

			var matches = 0;
			for (var i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo method && method == complete)
					matches++;
			}

			if (matches != 1)
				return codes;

			var result = new List<CodeInstruction>(codes.Count);
			foreach (var code in codes)
			{
				if (code.opcode == OpCodes.Call && code.operand is MethodInfo method && method == complete)
				{
					result.Add(new CodeInstruction(OpCodes.Call, replacement));
					continue;
				}

				result.Add(code);
			}

			_lastMatched = true;
			return result;
		}

		private static IEnumerable<CodeInstruction> InsertEarlyJobStarts(IEnumerable<CodeInstruction> instructions)
		{
			_lastMatched = false;
			var codes = new List<CodeInstruction>(instructions);

			var setFallShaderGlobals = AccessTools.Method(typeof(PlantFallColors), nameof(PlantFallColors.SetFallShaderGlobals));
			var doorsDebug = AccessTools.Method(typeof(DoorsDebugDrawer), nameof(DoorsDebugDrawer.DrawDebug));
			var startCulling = AccessTools.Method(typeof(ParallelPreDrawHelper),
				nameof(ParallelPreDrawHelper.StartCullingJob), new[] { typeof(Map) });
			var startPreDraw = AccessTools.Method(typeof(ParallelPreDrawHelper),
				nameof(ParallelPreDrawHelper.StartPreDrawJob), new[] { typeof(Map) });
			if (setFallShaderGlobals == null || doorsDebug == null || startCulling == null || startPreDraw == null)
				return codes;

			if (HasCall(codes, startCulling) || HasCall(codes, startPreDraw))
			{
				_lastMatched = true;
				return codes;
			}

			var cullSites = 0;
			var preDrawSites = 0;
			foreach (var code in codes)
			{
				if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method))
					continue;

				if (method == setFallShaderGlobals)
					cullSites++;
				else if (method == doorsDebug)
					preDrawSites++;
			}

			if (cullSites != 1 || preDrawSites != 1)
				return codes;

			var result = new List<CodeInstruction>(codes.Count + 4);
			foreach (var code in codes)
			{
				result.Add(code);

				if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method))
					continue;

				if (method == setFallShaderGlobals)
				{
					result.Add(new CodeInstruction(OpCodes.Ldarg_0));
					result.Add(new CodeInstruction(OpCodes.Call, startCulling));
				}
				else if (method == doorsDebug)
				{
					result.Add(new CodeInstruction(OpCodes.Ldarg_0));
					result.Add(new CodeInstruction(OpCodes.Call, startPreDraw));
				}
			}

			_lastMatched = true;
			return result;
		}
	}
}