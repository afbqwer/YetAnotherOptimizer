#if YAOPT_XML_TIMING
// Diagnostic XML def-load timing probes. This whole file is compiled only when the
// YAOPT_XML_TIMING symbol is defined:
//
//   dotnet build Source/YaOpt.sln -c Release -p:YaOptXmlTiming=true
//
// or set <YaOptXmlTiming>true</YaOptXmlTiming> in the gitignored Source/UserSettings.props.
// A normal build leaves this file empty; keep the probes out of shipped builds.
//
// It patches the vanilla def/XML loading pipeline in place (no optimization, no setting) and
// prints one timing table per phase group. All probes must be [EarlyPatch] because the def load
// runs in LoadedModManager.LoadAllActiveMods right after CreateModClasses, well before the normal
// Patcher ([StaticConstructorOnStartup]) applies anything.
//
// DefDatabase<T> is intentionally NOT patched per closed type: enumerating Def subclasses and
// harmony-patching hundreds of generic methods from the Mod constructor stalls startup. Its two
// multi-threaded blocks are approximated with cheap checkpoints instead.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Patches.Early
{
	internal static class XmlLoadTiming
	{
		private sealed class Stat
		{
			public long FirstStart = long.MaxValue;
			public long LastEnd;
			public long Total;
			public int Calls;
		}

		private static readonly object Sync = new object();
		private static readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
		private static readonly List<string> Order = new List<string>();
		private static readonly HashSet<string> WallKeys = new HashSet<string>();
		private static readonly Dictionary<string, long> Marks = new Dictionary<string, long>();

		internal static void Add(string name, long start)
		{
			var end = Stopwatch.GetTimestamp();
			lock (Sync)
			{
				var stat = Get(name);
				stat.Calls++;
				stat.Total += end - start;
				if (start < stat.FirstStart)
					stat.FirstStart = start;
				if (end > stat.LastEnd)
					stat.LastEnd = end;
			}
		}

		// For methods that run on several worker threads. The meaningful value is wall time from the
		// first start to the last end, not the sum of per-call durations.
		internal static void AddWall(string name, long start)
		{
			var end = Stopwatch.GetTimestamp();
			lock (Sync)
			{
				var stat = Get(name);
				stat.Calls++;
				WallKeys.Add(name);
				if (start < stat.FirstStart)
					stat.FirstStart = start;
				if (end > stat.LastEnd)
					stat.LastEnd = end;
			}
		}

		internal static void Mark(string name)
		{
			lock (Sync)
			{
				Marks[name] = Stopwatch.GetTimestamp();
			}
		}

		// Consumes the mark so only the first matching boundary measures it.
		internal static void Since(string mark, string resultName)
		{
			long start;
			lock (Sync)
			{
				if (!Marks.TryGetValue(mark, out start))
					return;
				Marks.Remove(mark);
			}
			AddWall(resultName, start);
		}

		// Replaces PatchOperation.Apply inside LoadedModManager.ApplyPatches so each top-level patch
		// operation type gets its own bucket. Nested sub-operations (e.g. PatchOperationSequence)
		// stay inside their parent's time on purpose.
		internal static bool TimedApply(PatchOperation operation, XmlDocument xml)
		{
			var name = "PatchOp: " + (operation != null ? operation.GetType().Name : "null");
			var start = Stopwatch.GetTimestamp();
			try
			{
				return operation.Apply(xml);
			}
			finally
			{
				Add(name, start);
			}
		}

		internal static void Dump(string title)
		{
			var sb = new StringBuilder();
			lock (Sync)
			{
				sb.AppendLine("[YaOpt-Timing] ===== " + title + " =====");
				var perMs = Stopwatch.Frequency / 1000.0;
				for (var i = 0; i < Order.Count; i++)
				{
					var name = Order[i];
					var stat = Stats[name];
					if (WallKeys.Contains(name))
					{
						var wall = (stat.LastEnd - stat.FirstStart) / perMs;
						sb.AppendLine("[YaOpt-Timing] " + name + ": " + wall.ToString("F1") +
							" ms wall, " + stat.Calls + " calls");
					}
					else
					{
						var ms = stat.Total / perMs;
						sb.AppendLine("[YaOpt-Timing] " + name + ": " + ms.ToString("F1") +
							" ms, " + stat.Calls + " calls");
					}
				}
				Stats.Clear();
				Order.Clear();
				WallKeys.Clear();
			}
			Log.Message(sb.ToString());
		}

		private static Stat Get(string name)
		{
			if (!Stats.TryGetValue(name, out var stat))
			{
				stat = new Stat();
				Stats[name] = stat;
				Order.Add(name);
			}
			return stat;
		}
	}

	/// <summary>
	/// Times every regular (non-recursive) method of the def load pipeline once per call and labels
	/// the entry after its declaring type. Recursive methods are handled by the two dedicated classes
	/// below so their nested frames are only counted once.
	/// </summary>
	[EarlyPatch]
	[HarmonyPatch]
	internal static class TempTiming_Methods
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			var targets = new[]
			{
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML)),
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.CombineIntoUnifiedXML)),
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ErrorCheckPatches)),
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches)),
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)),
				AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ClearCachedPatches)),
				AccessTools.Method(typeof(TKeySystem), nameof(TKeySystem.Parse)),
				AccessTools.Method(typeof(TKeySystem), nameof(TKeySystem.BuildMappings)),
				AccessTools.Method(typeof(ModContentPack), "LoadPatches"),
				AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.Resolve)),
				AccessTools.Method(typeof(XmlInheritance), "ResolveParentsAndChildNodesLinks"),
				AccessTools.Method(typeof(XmlInheritance), "ResolveXmlNodes"),
				AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.TryRegister)),
				AccessTools.Method(typeof(XmlInheritance), "ResolveXmlNodeFor"),
				AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.GetResolvedNodeFor)),
				AccessTools.Method(typeof(DirectXmlToObjectNew), nameof(DirectXmlToObjectNew.DefFromNodeNew)),
				AccessTools.Method(typeof(DirectXmlLoader), nameof(DirectXmlLoader.DefFromNode)),
				AccessTools.Method(typeof(DirectXmlCrossRefLoader),
					nameof(DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences)),
				AccessTools.Method(typeof(DefOfHelper), nameof(DefOfHelper.RebindAllDefOfs)),
				AccessTools.Method(typeof(BackstoryTranslationUtility),
					nameof(BackstoryTranslationUtility.LoadAndInjectBackstoryData)),
				AccessTools.Method(typeof(LoadedLanguage), nameof(LoadedLanguage.InjectIntoData_BeforeImpliedDefs)),
				AccessTools.Method(typeof(LoadedLanguage), nameof(LoadedLanguage.InjectIntoData_AfterImpliedDefs)),
				AccessTools.Method(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve)),
				AccessTools.Method(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PostResolve)),
				AccessTools.Method(typeof(StaticConstructorOnStartupUtility),
					nameof(StaticConstructorOnStartupUtility.CallAll)),
			};

			for (var i = 0; i < targets.Length; i++)
			{
				if (targets[i] != null)
					yield return targets[i];
			}
		}

		static void Prefix(MethodBase __originalMethod, out long __state)
		{
			__state = Stopwatch.GetTimestamp();
			// Split ParseAndProcessXML into "everything before the def loop" and the loop itself.
			// The first DefFromNodeNew call marks the loop start; pre-loop includes the third-party
			// PostInheritanceOperation pass.
			if (__originalMethod.Name == "ParseAndProcessXML")
				XmlLoadTiming.Mark("ppStart");
			else if (__originalMethod.Name == "DefFromNodeNew")
				XmlLoadTiming.Since("ppStart", "ParseAndProcessXML pre-loop (approx wall)");
		}

		static void Postfix(MethodBase __originalMethod, long __state)
		{
			XmlLoadTiming.Add(Label(__originalMethod), __state);
		}

		private static string Label(MethodBase method)
		{
			var type = method.DeclaringType;
			return (type != null ? type.Name : "?") + "." + method.Name;
		}
	}

	// Inclusive timing: outer call counts the whole recursive subtree; nested frames are skipped so the
	// number is not multiplied by the recursion depth.
	[EarlyPatch]
	[HarmonyPatch(typeof(XmlInheritance), "RecursiveNodeCopyOverwriteElements")]
	internal static class TempTiming_RecursiveMerge
	{
		[ThreadStatic] private static int _depth;

		static bool Prepare()
		{
			return AccessTools.Method(typeof(XmlInheritance), "RecursiveNodeCopyOverwriteElements") != null;
		}

		static void Prefix(out long __state)
		{
			__state = 0;
			if (_depth++ == 0)
				__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			var depth = --_depth;
			if (depth == 0 && __state != 0)
				XmlLoadTiming.Add("XmlInheritance.RecursiveMerge (inclusive)", __state);
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(XmlInheritance), "CheckForDuplicateNodes")]
	internal static class TempTiming_DuplicateCheck
	{
		[ThreadStatic] private static int _depth;

		static bool Prepare()
		{
			return AccessTools.Method(typeof(XmlInheritance), "CheckForDuplicateNodes") != null;
		}

		static void Prefix(out long __state)
		{
			__state = 0;
			if (_depth++ == 0)
				__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			var depth = --_depth;
			if (depth == 0 && __state != 0)
				XmlLoadTiming.Add("XmlInheritance.CheckForDuplicateNodes (inclusive)", __state);
		}
	}

	// DefDatabase<T>.AddAllInMods has no single entry point (it is called per closed generic type from
	// a Parallel.ForEach), so approximate its wall time between these two cheap boundaries.
	[EarlyPatch]
	[HarmonyPatch(typeof(LanguageDatabase), nameof(LanguageDatabase.InitAllMetadata))]
	internal static class TempTiming_MarkDbStart
	{
		static void Postfix()
		{
			XmlLoadTiming.Mark("db");
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(DirectXmlCrossRefLoader),
		nameof(DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences))]
	internal static class TempTiming_MeasureDb
	{
		static void Prefix()
		{
			XmlLoadTiming.Since("db", "DefDatabase<T>.AddAllInMods (approx wall)");
		}
	}

	// Same idea for the "Resolve references" block: mark after the final DefOf rebind, measure at the
	// shader registry pass that follows it.
	[EarlyPatch]
	[HarmonyPatch(typeof(DefOfHelper), nameof(DefOfHelper.RebindAllDefOfs))]
	internal static class TempTiming_MarkResolveRefsStart
	{
		static void Postfix()
		{
			XmlLoadTiming.Mark("resolveRefs");
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(ShaderDefRegistry), nameof(ShaderDefRegistry.RegisterAll))]
	internal static class TempTiming_MeasureResolveRefs
	{
		static void Prefix()
		{
			XmlLoadTiming.Since("resolveRefs", "DefDatabase<T>.ResolveAllReferences (approx wall)");
		}
	}

	/// <summary>
	/// Rewrites the single <c>item.Apply(xmlDoc)</c> call inside ApplyPatches so each top-level
	/// operation is timed and bucketed by its concrete type. The stack shape (instance + document)
	/// is identical for the static replacement, so no extra IL is needed.
	/// </summary>
	[EarlyPatch]
	[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
	internal static class TempTiming_PatchOperationApply
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = AccessTools.Method(typeof(XmlLoadTiming), nameof(XmlLoadTiming.TimedApply),
				new[] { typeof(PatchOperation), typeof(XmlDocument) });

			foreach (var instruction in instructions)
			{
				if (replacement != null &&
					(instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
					instruction.operand is MethodInfo method &&
					method.Name == "Apply" &&
					method.DeclaringType == typeof(PatchOperation) &&
					method.GetParameters().Length == 1 &&
					method.GetParameters()[0].ParameterType == typeof(XmlDocument))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
				}
				yield return instruction;
			}
		}
	}

	// "PostInheritanceOperation" (Patch Operation Collection) transpiles ParseAndProcessXML to run an
	// extra pass over the unified document. It is not vanilla code, so it only shows up as a gap.
	// Probe it reflectively and tolerate the mod being absent.
	[EarlyPatch]
	[ManualPatch]
	internal static class TempTiming_PostInheritance
	{
		internal static bool Patch(Harmony harmony)
		{
			Type type = null;
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (var i = 0; i < assemblies.Length; i++)
			{
				try
				{
					type = assemblies[i].GetType("PostInheritanceOperation.PatchQueue", false);
				}
				catch (Exception)
				{
					continue;
				}
				if (type != null)
					break;
			}

			if (type == null)
			{
				YaOptMod.Log("[YaOpt-Timing] PostInheritanceOperation not found; probe inactive.");
				return true;
			}

			var apply = AccessTools.Method(type, "ApplyPatches", new[] { typeof(System.Xml.XmlDocument) });
			if (apply != null)
			{
				harmony.Patch(apply,
					prefix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(ApplyPrefix)),
					postfix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(ApplyPostfix)));
			}

			var insert = AccessTools.Method(type, "ApplyXmlInheritanceOnDocument",
				new[] { typeof(System.Xml.XmlDocument) });
			if (insert != null)
			{
				harmony.Patch(insert,
					prefix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(InsertPrefix)),
					postfix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(InsertPostfix)));
			}

			YaOptMod.Log("[YaOpt-Timing] PostInheritanceOperation probes installed.");
			return true;
		}

		static void ApplyPrefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void ApplyPostfix(long __state)
		{
			XmlLoadTiming.Add("PostInheritanceOperation.ApplyPatches", __state);
		}

		static void InsertPrefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void InsertPostfix(long __state)
		{
			XmlLoadTiming.Add("PostInheritanceOperation.ApplyXmlInheritanceOnDocument", __state);
		}
	}

	// Dumped right after the final XmlInheritance.Clear, i.e. at the end of LoadAllActiveMods.
	[EarlyPatch]
	[HarmonyPatch(typeof(XmlInheritance), nameof(XmlInheritance.Clear))]
	internal static class TempTiming_DumpXml
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			XmlLoadTiming.Add("XmlInheritance.Clear (final)", __state);
			XmlLoadTiming.Dump("XML def load (LoadAllActiveMods)");
		}
	}

	// Dumped after short hashes, i.e. after DefDatabase, cross-ref, implied defs and ResolveReferences.
	[EarlyPatch]
	[HarmonyPatch(typeof(ShortHashGiver), nameof(ShortHashGiver.GiveAllShortHashes))]
	internal static class TempTiming_DumpDefPipeline
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			XmlLoadTiming.Add("ShortHashGiver.GiveAllShortHashes", __state);
			XmlLoadTiming.Dump("Def pipeline (databases, cross-ref, implied defs, resolve refs)");
		}
	}

	// Dumped before the final GC, i.e. after static ctors, deferred translation and atlas baking.
	[EarlyPatch]
	[HarmonyPatch(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases))]
	internal static class TempTiming_DumpLate
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			XmlLoadTiming.Add("GlobalTextureAtlasManager.BakeStaticAtlases", __state);
			XmlLoadTiming.Dump("Late load (static ctors, translation, atlas; GC not included)");
		}
	}
}
#endif
