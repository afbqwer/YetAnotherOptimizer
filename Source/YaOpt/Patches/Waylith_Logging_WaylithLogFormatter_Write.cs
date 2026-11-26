using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace YaOpt.Patches
{
	/// <summary>
	/// Skips stack-trace capture for Info-level entries in the Waylith log formatter.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WaylithLogFormatter.Write&lt;TState&gt; is a generic override on a non-generic class. Harmony
	/// cannot patch the open definition, so the target is the closed
	/// <c>Write&lt;FormattedLogValues&gt;</c> instantiation used by every ILogger extension call.
	/// </para>
	/// <para>
	/// The transpiler inserts <c>if (logEntry.LogLevel == Information) goto nullStack;</c> before
	/// the formatter's <c>EnableStackTraces || IsCapturingStacks || HasListeners</c> condition.
	/// Warning and Error keep their stacks. If the expected IL is missing, the original method is
	/// left untouched.
	/// </para>
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptInfoLogNoStack"/>
	[HarmonyPatch]
	internal static class Waylith_Logging_WaylithLogFormatter_Write
	{
		private const int InformationLevel = 2;

		private static MethodBase _target;
		private static FieldInfo _enableStackTracesField;
		private static MethodInfo _logLevelGetter;
		private static bool _warned;

		private static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptInfoLogNoStack.Enabled == true && Resolve();
		}

		private static MethodBase TargetMethod()
		{
			return _target;
		}

		private static bool Resolve()
		{
			if (_target != null)
				return true;

			try
			{
				var formatter = AccessTools.TypeByName("Waylith.Logging.WaylithLogFormatter");
				var state = AccessTools.TypeByName("Microsoft.Extensions.Logging.FormattedLogValues");
				var logEntryOpen = AccessTools.TypeByName("Microsoft.Extensions.Logging.Abstractions.LogEntry`1");
				if (formatter == null || state == null || logEntryOpen == null)
				{
					Warn("Waylith formatter or logging types were not found");
					return false;
				}

				var open = formatter.GetMethods(BindingFlags.Public | BindingFlags.Instance |
					BindingFlags.DeclaredOnly)
					.FirstOrDefault(m => m.Name == "Write" && m.IsGenericMethodDefinition &&
										m.GetParameters().Length == 3);
				if (open == null)
				{
					Warn("WaylithLogFormatter.Write<TState> was not found");
					return false;
				}

				_target = open.MakeGenericMethod(state);
				_enableStackTracesField = formatter.GetField("EnableStackTraces",
					BindingFlags.Public | BindingFlags.Static);
				_logLevelGetter = AccessTools.PropertyGetter(logEntryOpen.MakeGenericType(state), "LogLevel");

				if (_enableStackTracesField == null || _logLevelGetter == null)
				{
					Warn("formatter stack-trace members were not found");
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				Warn("resolving formatter members threw: " + ex);
				return false;
			}
		}

		private static IEnumerable<CodeInstruction> Transpiler(
			IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator)
		{
			var codes = new List<CodeInstruction>(instructions);
			if (_enableStackTracesField == null || _logLevelGetter == null)
			{
				Warn("formatter members are unresolved");
				return codes;
			}

			var anchor = -1;
			for (var i = 0; i < codes.Count; i++)
			{
				if (IsEnableStackTracesLoad(codes[i]))
				{
					anchor = i;
					break;
				}
			}

			if (anchor < 0)
			{
				Warn("EnableStackTraces load was not found");
				return codes;
			}

			var nullIndex = -1;
			for (var i = anchor + 1; i + 1 < codes.Count; i++)
			{
				if ((codes[i].opcode == OpCodes.Brtrue || codes[i].opcode == OpCodes.Brtrue_S) &&
					codes[i + 1].opcode == OpCodes.Ldnull)
				{
					nullIndex = i + 1;
					break;
				}
			}

			if (nullIndex < 0)
			{
				Warn("null stack-trace branch was not found");
				return codes;
			}

			var constructorFound = false;
			var limit = Math.Min(codes.Count, nullIndex + 9);
			for (var i = nullIndex; i < limit; i++)
			{
				if (IsStackTraceConstructor(codes[i]))
				{
					constructorFound = true;
					break;
				}
			}

			if (!constructorFound)
			{
				Warn("StackTrace constructor was not found");
				return codes;
			}

			var nullLabel = generator.DefineLabel();
			codes[nullIndex].labels.Add(nullLabel);

			var entryArgument = original.IsStatic
				? new CodeInstruction(OpCodes.Ldarg_0)
				: new CodeInstruction(OpCodes.Ldarg_1);

			// Move any labels so existing branches still pass through the level check.
			if (codes[anchor].labels.Count > 0)
			{
				entryArgument.labels.AddRange(codes[anchor].labels);
				codes[anchor].labels.Clear();
			}

			codes.InsertRange(anchor, new[]
			{
				entryArgument,
				new CodeInstruction(OpCodes.Call, _logLevelGetter),
				new CodeInstruction(OpCodes.Ldc_I4, InformationLevel),
				new CodeInstruction(OpCodes.Beq, nullLabel),
			});

			return codes;
		}

		private static bool IsEnableStackTracesLoad(CodeInstruction code)
		{
			if (code.opcode != OpCodes.Ldsfld || !(code.operand is FieldInfo field))
				return false;

			return field.Name == "EnableStackTraces" &&
				field.DeclaringType?.FullName == "Waylith.Logging.WaylithLogFormatter";
		}

		private static bool IsStackTraceConstructor(CodeInstruction code)
		{
			if (code.opcode != OpCodes.Newobj || !(code.operand is ConstructorInfo constructor))
				return false;

			var parameters = constructor.GetParameters();
			return constructor.DeclaringType == typeof(System.Diagnostics.StackTrace) &&
				parameters.Length == 2 &&
				parameters[0].ParameterType == typeof(int) &&
				parameters[1].ParameterType == typeof(bool);
		}

		private static void Warn(string message)
		{
			if (_warned)
				return;
			_warned = true;
			YaOptMod.Warning("[YaOpt] Info log stack suppression stays off: " + message + ".");
		}
	}
}