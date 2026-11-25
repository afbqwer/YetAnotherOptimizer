using Mono.Cecil;
using Mono.Cecil.Cil;
using Prepatcher;
using RimWorld;
using System;
using System.Reflection;
using Verse;
using YaOpt.Helpers;
using Instruction = Mono.Cecil.Cil.Instruction;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace YaOpt.Prepatch
{
	/// <summary>
	/// Load-time replacements installed by the built-in Prepatcher.
	/// </summary>
	/// <remarks>
	/// Each replacement delegates to <see cref="PrepatcherMethods"/>, which contains both the
	/// optimized path and a vanilla fallback gated by <see cref="PrepatcherState"/>.
	/// </remarks>
	internal static class PrepatcherFreePatches
	{
		[FreePatch]
		private static void PatchListerBuildings(ModuleDefinition module)
		{
			var type = module.GetType("Verse", "ListerBuildings") ??
					   throw new MissingTypeException("Verse.ListerBuildings");

			var all = FindInstanceMethod(type, nameof(ListerBuildings.AllBuildingsColonistOfDef),
				"Verse.ThingDef");
			var have = FindInstanceMethod(type, nameof(ListerBuildings.ColonistsHaveBuilding),
				"Verse.ThingDef");
			var powerOn = FindInstanceMethod(type,
				nameof(ListerBuildings.ColonistsHaveBuildingWithPowerOn), "Verse.ThingDef");

			Replace(module, all, Import(module, typeof(PrepatcherMethods),
				nameof(PrepatcherMethods.AllBuildingsColonistOfDef),
				typeof(ListerBuildings), typeof(ThingDef)));
			Replace(module, have, Import(module, typeof(PrepatcherMethods),
				nameof(PrepatcherMethods.ColonistsHaveBuilding),
				typeof(ListerBuildings), typeof(ThingDef)));
			Replace(module, powerOn, Import(module, typeof(PrepatcherMethods),
				nameof(PrepatcherMethods.ColonistsHaveBuildingWithPowerOn),
				typeof(ListerBuildings), typeof(ThingDef)));

			PrepatcherState.MarkListerPatched();
		}

		[FreePatch]
		private static void PatchImmunityHandler(ModuleDefinition module)
		{
			var type = module.GetType("Verse", "ImmunityHandler") ??
					   throw new MissingTypeException("Verse.ImmunityHandler");
			var method = FindInstanceMethod(type, nameof(ImmunityHandler.NeededImmunitiesNow));

			Replace(module, method, Import(module, typeof(PrepatcherMethods),
				nameof(PrepatcherMethods.NeededImmunitiesNow), typeof(ImmunityHandler)));

			PrepatcherState.MarkImmunityPatched();
		}

		[FreePatch]
		private static void PatchRefuelSearchSet(ModuleDefinition module)
		{
			var type = module.GetType("RimWorld", "WorkGiver_Scanner") ??
					   throw new MissingTypeException("RimWorld.WorkGiver_Scanner");
			var method = FindInstanceMethod(type, nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal),
				"Verse.Pawn");

			Replace(module, method, Import(module, typeof(PrepatcherMethods),
				nameof(PrepatcherMethods.PotentialWorkThingsGlobal),
				typeof(WorkGiver_Scanner), typeof(Pawn)));

			PrepatcherState.MarkRefuelPatched();
		}

		private static MethodReference Import(ModuleDefinition module, Type type, string name,
			params Type[] parameterTypes)
		{
			var method = type.GetMethod(name,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
				null, parameterTypes, null);
			if (method == null)
				throw new MissingMethodException(type.FullName, name);
			return module.ImportReference(method);
		}

		private static MethodDefinition FindInstanceMethod(TypeDefinition type, string name,
			params string[] parameterTypes)
		{
			foreach (var method in type.Methods)
			{
				if (method.IsStatic || method.Name != name ||
					method.Parameters.Count != parameterTypes.Length)
					continue;

				var match = true;
				for (var i = 0; i < parameterTypes.Length; i++)
				{
					if (method.Parameters[i].ParameterType.FullName != parameterTypes[i])
					{
						match = false;
						break;
					}
				}
				if (match)
					return method;
			}
			throw new MissingMethodException(type.FullName, name);
		}

		private static void Replace(ModuleDefinition module, MethodDefinition method,
			MethodReference helper)
		{
			var body = method.Body;
			body.Instructions.Clear();
			body.ExceptionHandlers.Clear();
			body.Variables.Clear();
			body.InitLocals = false;

			var il = body.GetILProcessor();
			if (!method.IsStatic)
				il.Emit(OpCodes.Ldarg_0);
			for (var i = 0; i < method.Parameters.Count; i++)
				il.Emit(OpCodes.Ldarg, method.Parameters[i]);
			il.Emit(OpCodes.Call, helper);
			il.Emit(OpCodes.Ret);

			body.MaxStackSize = Math.Max(body.MaxStackSize,
				method.Parameters.Count + (method.IsStatic ? 0 : 1));
		}

		private sealed class MissingTypeException : Exception
		{
			public MissingTypeException(string typeName)
				: base("Cannot find type " + typeName)
			{
			}
		}
	}
}