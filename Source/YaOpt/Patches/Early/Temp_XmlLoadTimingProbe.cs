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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using YaOpt.Helpers;

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
			return TimedApplyCore("PatchOp: ", operation, xml);
		}

		// Same idea for operations applied from inside a container. Nested sequences are timed too, so
		// the flattened subtree shows up per concrete type instead of only as the outer container.
		internal static bool TimedSubApply(PatchOperation operation, XmlDocument xml)
		{
			return TimedApplyCore("SubOp: ", operation, xml);
		}

		private static bool TimedApplyCore(string prefix, PatchOperation operation, XmlDocument xml)
		{
			var name = prefix + (operation != null ? operation.GetType().Name : "null");
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

		private static int _gc0;
		private static int _gc1;
		private static int _gc2;
		private static long _gcAllocated;

		// GC pressure is invisible in the wall-time buckets, but a phase that allocates hundreds of
		// thousands of short-lived nodes can spend most of its time in collections.
		internal static void MarkGc()
		{
			_gc0 = GC.CollectionCount(0);
			_gc1 = GC.CollectionCount(1);
			_gc2 = GC.CollectionCount(2);
			_gcAllocated = GC.GetTotalAllocatedBytes(false);
		}

		internal static void DumpGc(string label)
		{
			YaOptMod.Log("[YaOpt-Timing] " + label + " GC0=" + (GC.CollectionCount(0) - _gc0) +
				" GC1=" + (GC.CollectionCount(1) - _gc1) +
				" GC2=" + (GC.CollectionCount(2) - _gc2) +
				" alloc=" + ((GC.GetTotalAllocatedBytes(false) - _gcAllocated) / 1048576) + " MB");
		}

		internal static int CountThinkNodes(ThinkNode root)
		{
			if (root == null)
				return 0;
			var count = 0;
			var stack = new Stack<ThinkNode>();
			stack.Push(root);
			while (stack.Count > 0)
			{
				var node = stack.Pop();
				count++;
				for (var i = 0; i < node.subNodes.Count; i++)
					stack.Push(node.subNodes[i]);
			}
			return count;
		}

		internal static double ElapsedMs(long start)
		{
			return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
		}
	}

	// Stable fingerprint of the final unified XML, used to prove that the optimized patch paths select
	// the same nodes as the native engine. string.GetHashCode is randomized per process, so names are
	// hashed character by character instead. Compare the TOTAL line (and the per-type lines) between a
	// run with the fast options on and a run with them off.
	internal static class DocumentFingerprint
	{
		internal static void Dump(XmlDocument xmlDocument, string label)
		{
			if (xmlDocument == null || xmlDocument.DocumentElement == null)
				return;
			try
			{
				var types = new Dictionary<string, int[]>();
				var totalDefs = 0;
				var totalNodes = 0;
				var totalHash = 17;
				for (var child = xmlDocument.DocumentElement.FirstChild; child != null; child = child.NextSibling)
				{
					if (!(child is XmlElement def))
						continue;
					var defNameNode = def["defName"];
					var hash = StableHash(defNameNode != null ? defNameNode.InnerText : string.Empty);
					var nodes = HashNode(def, ref hash);
					if (!types.TryGetValue(def.Name, out var entry))
					{
						entry = new int[3];
						types[def.Name] = entry;
					}
					entry[0]++;
					entry[1] += nodes;
					entry[2] = unchecked(entry[2] * 31 + hash);
					totalDefs++;
					totalNodes += nodes;
					totalHash = unchecked(totalHash * 31 + hash);
				}

				var names = new List<string>(types.Keys);
				names.Sort(StringComparer.Ordinal);
				for (var i = 0; i < names.Count; i++)
				{
					var entry = types[names[i]];
					if (entry[0] >= 20)
						YaOptMod.Log("[YaOpt-FP] " + label + " " + names[i] + ": defs=" + entry[0] +
							", nodes=" + entry[1] + ", hash=" + entry[2]);
				}
				YaOptMod.Log("[YaOpt-FP] " + label + " TOTAL: defs=" + totalDefs + ", nodes=" + totalNodes +
					", hash=" + totalHash);
			}
			catch (Exception ex)
			{
				YaOptMod.Warning("[YaOpt-FP] fingerprint failed: " + ex);
			}
		}

		private static int HashNode(XmlNode node, ref int hash)
		{
			var count = 1;
			hash = unchecked(hash * 31 + StableHash(node.Name));
			var attributes = node.Attributes;
			if (attributes != null)
			{
				for (var i = 0; i < attributes.Count; i++)
				{
					var attribute = attributes[i];
					hash = unchecked(hash * 31 + StableHash(attribute.Name));
					hash = unchecked(hash * 31 + StableHash(attribute.Value));
				}
			}
			for (var child = node.FirstChild; child != null; child = child.NextSibling)
				count += HashNode(child, ref hash);
			return count;
		}

		private static int StableHash(string text)
		{
			if (text == null)
				return 0;
			var value = 23;
			for (var i = 0; i < text.Length; i++)
				value = unchecked(value * 31 + text[i]);
			return value;
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

	// Same idea for the "Resolve references" block. DefOfHelper.RebindAllDefOfs runs twice (early and
	// final); only the final pass is followed by the resolver blocks. Mark after the pre-resolve global
	// operations (ResetStaticDataPre) so the measured window starts at the ResolveAllReferences call.
	[EarlyPatch]
	[HarmonyPatch(typeof(PlayDataLoader), "ResetStaticDataPre")]
	internal static class TempTiming_MarkResolveRefsStart
	{
		static bool Prepare()
		{
			return AccessTools.Method(typeof(PlayDataLoader), "ResetStaticDataPre") != null;
		}

		static void Postfix()
		{
			XmlLoadTiming.Mark("resolveRefs");
			XmlLoadTiming.MarkGc();
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(ShaderDefRegistry), nameof(ShaderDefRegistry.RegisterAll))]
	internal static class TempTiming_MeasureResolveRefs
	{
		static void Prefix()
		{
			XmlLoadTiming.Since("resolveRefs", "DefDatabase<T>.ResolveAllReferences (approx wall)");
			XmlLoadTiming.DumpGc("ResolveAllReferences");
		}
	}

	// Splits the resolver window into its four vanilla blocks. The three named generic instantiations
	// are closed types, so they can be patched directly; the per-type loop over every other Def type
	// is measured as the gap between the RecipeDef and ThingDef blocks.
	[EarlyPatch]
	[HarmonyPatch(typeof(DefDatabase<ThingCategoryDef>), "ResolveAllReferences",
		new[] { typeof(bool), typeof(bool) })]
	internal static class TempTiming_ResolveCategory
	{
		static void Postfix()
		{
			XmlLoadTiming.Mark("resolveCat");
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(DefDatabase<RecipeDef>), "ResolveAllReferences",
		new[] { typeof(bool), typeof(bool) })]
	internal static class TempTiming_ResolveRecipe
	{
		static void Postfix()
		{
			XmlLoadTiming.Since("resolveCat", "Resolve: ThingCategoryDef block");
			XmlLoadTiming.Mark("resolveRecipe");
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(DefDatabase<ThingDef>), "ResolveAllReferences",
		new[] { typeof(bool), typeof(bool) })]
	internal static class TempTiming_ResolveThingDef
	{
		static void Prefix()
		{
			XmlLoadTiming.Since("resolveRecipe", "Resolve: remaining Def types block");
			XmlLoadTiming.Mark("resolveThing");
		}

		static void Postfix()
		{
			XmlLoadTiming.Since("resolveThing", "Resolve: ThingDef block");
		}
	}

	// The "remaining Def types" loop dispatches each type through this reflection helper. Bucketing the
	// call by genericParam attributes the 35 s block to individual Def types. Only ResolveAllReferences
	// calls are timed; every other caller pays just a string comparison.
	[EarlyPatch]
	[HarmonyPatch(typeof(GenGeneric), nameof(GenGeneric.InvokeStaticMethodOnGenericType),
		new[] { typeof(Type), typeof(Type), typeof(string), typeof(object[]) })]
	internal static class TempTiming_DefTypeResolve
	{
		static void Prefix(string methodName, out long __state)
		{
			__state = methodName == "ResolveAllReferences" ? Stopwatch.GetTimestamp() : 0;
		}

		static void Postfix(Type genericParam, string methodName, long __state)
		{
			if (__state != 0 && methodName == "ResolveAllReferences" && genericParam != null)
				XmlLoadTiming.Add("DefResolve: " + genericParam.Name, __state);
		}
	}

	// ThinkTreeDef was 35.8 s of the 37.9 s ResolveAllReferences phase. Bucketing by defName shows
	// whether one tree (or a shared subtree) is responsible or the cost is spread over every tree.
	[EarlyPatch]
	[HarmonyPatch(typeof(ThinkTreeDef), nameof(ThinkTreeDef.ResolveReferences))]
	internal static class TempTiming_ThinkTreeDef
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(ThinkTreeDef __instance, long __state)
		{
			var name = __instance != null ? __instance.defName : "null";
			XmlLoadTiming.Add("ThinkTree: " + name, __state);
			var ms = XmlLoadTiming.ElapsedMs(__state);
			if (ms >= 30)
			{
				YaOptMod.Log("[YaOpt-Timing] ThinkTree " + name + ": " + ms.ToString("F1") +
					" ms, nodes=" + XmlLoadTiming.CountThinkNodes(__instance.thinkRoot));
			}
		}
	}

	// The four steps of ThinkTreeDef.ResolveReferences. Def.ResolveReferences covers the base call
	// (def mod extensions); ThinkNode.ResolveReferences is added for the base body and for every
	// subclass override, so a single slow node type shows up by name.
	[EarlyPatch]
	[HarmonyPatch]
	internal static class TempTiming_ThinkTreeSteps
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Def), nameof(Def.ResolveReferences));
			yield return AccessTools.Method(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignKeys));
			var resolveParents = AccessTools.Method(typeof(ThinkTreeDef), "ResolveParentNodes",
				new[] { typeof(ThinkNode) });
			if (resolveParents != null)
				yield return resolveParents;
			yield return AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.ResolveReferences));
			foreach (var type in typeof(ThinkNode).AllSubclasses())
			{
				var method = AccessTools.DeclaredMethod(type, nameof(ThinkNode.ResolveReferences));
				if (method != null && method.DeclaringType == type)
					yield return method;
			}
		}

		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(object __instance, MethodBase __originalMethod, long __state)
		{
			var declaringType = __originalMethod.DeclaringType;
			string name;
			if (declaringType == typeof(Def))
				name = "DefBaseRef: " + (__instance != null ? __instance.GetType().Name : "null");
			else if (declaringType != null && typeof(ThinkNode).IsAssignableFrom(declaringType))
				name = "ThinkNodeRef: " + (__instance != null ? __instance.GetType().Name : declaringType.Name);
			else
				name = "ThinkStep: " + declaringType?.Name + "." + __originalMethod.Name;
			XmlLoadTiming.Add(name, __state);
		}
	}

	// ThinkNode_Subtree.ResolveSubnodes deep copies the referenced tree, and nested subtree nodes copy
	// their own tree in turn. Bucketing by the referenced defName shows which tree multiplies.
	// ResolveSubnodesAndRecur is recursive, so only its outermost call is recorded.
	[EarlyPatch]
	[HarmonyPatch]
	internal static class TempTiming_ThinkSubnodes
	{
		private static int _depth;
		private static int _subtreeDepth;
		private static long _outerStart;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.ResolveSubnodesAndRecur));
			var subtree = AccessTools.Method(typeof(ThinkNode_Subtree), "ResolveSubnodes");
			if (subtree != null)
				yield return subtree;
		}

		static void Prefix(MethodBase __originalMethod, out long __state)
		{
			__state = 0;
			if (__originalMethod.DeclaringType == typeof(ThinkNode))
			{
				if (_depth++ == 0)
					_outerStart = Stopwatch.GetTimestamp();
				return;
			}
			_subtreeDepth++;
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(object __instance, MethodBase __originalMethod, long __state)
		{
			if (__originalMethod.DeclaringType == typeof(ThinkNode))
			{
				if (--_depth == 0)
					XmlLoadTiming.Add("ThinkSubnodes: total", _outerStart);
				return;
			}

			_subtreeDepth--;
			var subtree = __instance as ThinkNode_Subtree;
			var tree = subtree != null ? subtree.treeDef : null;
			var name = tree != null ? tree.defName : "null";
			XmlLoadTiming.Add("ThinkSubtree: " + name, __state);
			YaOptMod.Log("[YaOpt-Timing] ThinkSubtree " + name + ": " +
				XmlLoadTiming.ElapsedMs(__state).ToString("F1") +
				" ms, copied nodes=" +
				(subtree != null ? XmlLoadTiming.CountThinkNodes(subtree.subtreeNode) : -1) +
				", nested=" + _subtreeDepth);
		}
	}

	// ThinkNode.DeepCopy assigns a save key to every copied node through this helper; the hash set
	// lookup and Rand state push are inside the DeepCopy cost measured by the subtree buckets.
	[EarlyPatch]
	[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignSingleKey))]
	internal static class TempTiming_AssignSingleKey
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(long __state)
		{
			XmlLoadTiming.Add("ThinkKeyAssign.Single", __state);
		}
	}

	// Compares the fast key assignment with a faithful replay of the vanilla retry loop on a sample of
	// calls. The replay runs before the production prefix adds the key and leaves the Rand state
	// untouched (PushState/PopState around it), so expected and actual can be compared directly.
	[EarlyPatch]
	[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignSingleKey))]
	internal static class TempTiming_ThinkKeyVerify
	{
		private static int _calls;
		private static int _sampled;
		private static int _mismatches;
		private static int _expected;
		private static int _expectedRetries;
		private static bool _check;

		static void Prefix(ThinkNode node, int startHash)
		{
			_check = false;
			if (startHash != 0 || node == null)
				return;
			var call = _calls++;
			// The vanilla loop is quadratic, so sample early calls densely and later calls sparsely.
			if (call >= 200 && call % 500 != 0)
				return;

			var keys = ThinkTreeKeyAssigner.assignedKeys;
			var key = 0;
			for (var current = node; current != null; current = current.parent)
				key = Gen.HashCombineInt(key, GenText.StableStringHash(current.GetType().Name));

			var retries = 0;
			Rand.PushState(0);
			try
			{
				while (keys.Contains(key))
				{
					key ^= Rand.Int;
					retries++;
				}
			}
			finally
			{
				Rand.PopState();
			}

			_expected = key;
			_expectedRetries = retries;
			_check = true;
			_sampled++;
		}

		static void Postfix(ThinkNode node)
		{
			if (!_check)
				return;
			_check = false;
			if (node != null && node.UniqueSaveKey == _expected)
				return;
			_mismatches++;
			if (_mismatches <= 10)
			{
				YaOptMod.Log("[YaOpt-Test] key mismatch: type=" +
					(node != null ? node.GetType().Name : "null") +
					" expected=" + _expected +
					" actual=" + (node != null ? node.UniqueSaveKey.ToString() : "null") +
					" vanillaRetries=" + _expectedRetries +
					" assigned=" + ThinkTreeKeyAssigner.assignedKeys.Count);
			}
		}

		internal static void Dump()
		{
			YaOptMod.Log("[YaOpt-Test] key verify: calls=" + _calls + " sampled=" + _sampled +
				" mismatches=" + _mismatches);
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

	/// <summary>
	/// Same replacement for the per-child <c>operation.Apply(xml)</c> call inside sequences. A deferred
	/// sequence can hide most of the patch time, so its children are bucketed by concrete type too.
	/// </summary>
	[EarlyPatch]
	[HarmonyPatch(typeof(PatchOperationSequence), "ApplyWorker")]
	internal static class TempTiming_SequenceChildren
	{
		static bool Prepare()
		{
			return AccessTools.Method(typeof(PatchOperationSequence), "ApplyWorker") != null;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = AccessTools.Method(typeof(XmlLoadTiming), nameof(XmlLoadTiming.TimedSubApply),
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

			// ApplyPatches runs the merge first and then dequeues the operations that PostInheritanceOperation.Patch
			// deferred. Those deferred operations are not visible in the LoadedModManager.ApplyPatches buckets, so
			// they get their own per-type buckets here.
			var applyPatch = AccessTools.Method(type, "ApplyPatch");
			if (applyPatch != null)
			{
				harmony.Patch(applyPatch,
					prefix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(DeferredPrefix)),
					postfix: new HarmonyMethod(typeof(TempTiming_PostInheritance), nameof(DeferredPostfix)));
			}

			YaOptMod.Log("[YaOpt-Timing] PostInheritanceOperation probes installed.");
			return true;
		}

		private struct DeferredState
		{
			public long Start;
			public TimeSpan GcPause;
			public long Allocated;
		}

		private static DeferredState Capture()
		{
			return new DeferredState
			{
				Start = Stopwatch.GetTimestamp(),
				GcPause = GC.GetTotalPauseDuration(),
				Allocated = GC.GetTotalAllocatedBytes(false)
			};
		}

		private static string DescribeGc(DeferredState state)
		{
			var pause = GC.GetTotalPauseDuration() - state.GcPause;
			var allocated = (GC.GetTotalAllocatedBytes(false) - state.Allocated) / 1048576;
			return "gcPause=" + pause.TotalMilliseconds.ToString("F0") + " ms, alloc=" + allocated + " MB";
		}

		static void DeferredPrefix(PatchOperation patch, XmlDocument resolvedXmlDocument, string modIdentifier,
			out DeferredState __state)
		{
			__state = Capture();
			try
			{
				var pathed = patch as PatchOperationPathed;
				YaOptMod.Log("[YaOpt-Timing] deferred " +
					(patch != null ? patch.GetType().Name : "null") +
					" " + XPathCachePeek.Describe(resolvedXmlDocument) +
					" mod='" + modIdentifier + "'" +
					" xpath='" + (pathed != null ? pathed.xpath : "?") + "'" +
					" file='" + (patch != null ? patch.sourceFile : "?") + "'");
			}
			catch (Exception)
			{
			}
		}

		static void DeferredPostfix(PatchOperation patch, DeferredState __state)
		{
			var ms = XmlLoadTiming.ElapsedMs(__state.Start);
			try
			{
				var pathed = patch as PatchOperationPathed;
				YaOptMod.Log("[YaOpt-Timing] deferred " +
					(patch != null ? patch.GetType().Name : "null") + " took " + ms.ToString("F1") +
					" ms, " + DescribeGc(__state) +
					" xpath='" + (pathed != null ? pathed.xpath : "?") + "'");
			}
			catch (Exception)
			{
			}
			XmlLoadTiming.Add("Deferred: " + (patch != null ? patch.GetType().Name : "null"), __state.Start);
		}

		static void ApplyPrefix(out DeferredState __state)
		{
			__state = Capture();
		}

		static void ApplyPostfix(DeferredState __state, XmlDocument xmlDocument)
		{
			XmlLoadTiming.Add("PostInheritanceOperation.ApplyPatches", __state.Start);
			YaOptMod.Log("[YaOpt-Timing] PostInheritanceOperation.ApplyPatches took " +
				XmlLoadTiming.ElapsedMs(__state.Start).ToString("F0") + " ms, " + DescribeGc(__state));
			DocumentFingerprint.Dump(xmlDocument, "post-deferred");
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

	// ThinkSubtree's DeepCopy is 35 s. Bucket every DeepCopy call by the copied node type; calls nest,
	// so the totals overlap, but a type that dominates is immediately visible.
	[EarlyPatch]
	[HarmonyPatch]
	internal static class TempTiming_ThinkDeepCopy
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.DeepCopy));
			foreach (var type in typeof(ThinkNode).AllSubclasses())
			{
				var method = AccessTools.DeclaredMethod(type, nameof(ThinkNode.DeepCopy));
				if (method != null && method.DeclaringType == type)
					yield return method;
			}
		}

		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(object __instance, MethodBase __originalMethod, long __state)
		{
			var type = __instance != null ? __instance.GetType() : __originalMethod.DeclaringType;
			XmlLoadTiming.Add("DeepCopy: " + (type != null ? type.Name : "null"), __state);
		}
	}

	// Reads the reducer's private state so the probe can tell whether a cache really exists at a
	// given point in the load, instead of inferring it from timing.
	internal static class XPathCachePeek
	{
		private static readonly FieldInfo AvailableField =
			AccessTools.Field(typeof(XPathReducer), "_available");
		private static readonly FieldInfo DefsField =
			AccessTools.Field(typeof(XPathReducer), "_xmlDefs");

		internal static bool Available =>
			AvailableField != null && AvailableField.GetValue(null) is bool value && value;

		internal static XmlElement Defs =>
			DefsField != null ? DefsField.GetValue(null) as XmlElement : null;

		internal static string Describe(XmlDocument xml)
		{
			var defs = Defs;
			if (defs == null)
				return "cache=<none>";
			var same = xml != null ? (defs.OwnerDocument == xml).ToString() : "?";
			return "cache=" + (Available ? "on" : "off") +
				", defsAttached=" + (defs.ParentNode != null) +
				", docSame=" + same +
				", defsChildren=" + defs.ChildNodes.Count;
		}
	}

	// Two markers around LoadedModManager.ApplyPatches. If the first runs and the second does not,
	// another mod's prefix returned false and Harmony skipped every remaining prefix, including the
	// one that builds the XPath cache.
	[EarlyPatch]
	[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
	[HarmonyPriority(900)]
	internal static class TempTiming_ApplyPatchesHigh
	{
		static void Prefix(XmlDocument xmlDoc)
		{
			YaOptMod.Log("[YaOpt-Timing] ApplyPatches prefix@900: defs=" +
				(xmlDoc != null && xmlDoc["Defs"] != null) + ", " + XPathCachePeek.Describe(xmlDoc));
			XmlLoadTiming.MarkGc();
		}

		static void Postfix()
		{
			YaOptMod.Log("[YaOpt-Timing] ApplyPatches postfix@900: " + XPathCachePeek.Describe(null));
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
	[HarmonyPriority(100)]
	internal static class TempTiming_ApplyPatchesLow
	{
		static void Prefix()
		{
			YaOptMod.Log("[YaOpt-Timing] ApplyPatches prefix@100: " + XPathCachePeek.Describe(null) +
				" (missing line means an earlier prefix skipped the rest)");
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(XPathReducer), nameof(XPathReducer.CreateCache))]
	internal static class TempTiming_XPathCacheCreate
	{
		static void Prefix(XmlDocument xml)
		{
			YaOptMod.Log("[YaOpt-Timing] CreateCache called: root=" +
				(xml != null && xml.DocumentElement != null ? xml.DocumentElement.Name : "null") +
				", defsChildren=" + (xml?["Defs"]?.ChildNodes.Count ?? -1));
		}

		static void Postfix()
		{
			YaOptMod.Log("[YaOpt-Timing] CreateCache done: " + XPathCachePeek.Describe(null));
		}
	}

	[EarlyPatch]
	[HarmonyPatch(typeof(XPathReducer), nameof(XPathReducer.ClearCache))]
	internal static class TempTiming_XPathCacheClear
	{
		static void Prefix()
		{
			YaOptMod.Log("[YaOpt-Timing] XPathReducer.ClearCache called:\n" + Environment.StackTrace);
		}
	}

	// GetXmlEnumerator is the only XPath entry point used by the patch operations. Native SelectNodes
	// returns a lazily evaluated list, so the predicate queries are wrapped to capture the enumeration
	// time and the result count as well.
	[EarlyPatch]
	[HarmonyPatch(typeof(XPathReducer), nameof(XPathReducer.GetXmlEnumerator))]
	internal static class TempTiming_XPathEnumeration
	{
		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(string xpath, long __state, ref IEnumerator __result)
		{
			var cached = XPathCachePeek.Available;
			var predicate = xpath != null &&
				(xpath.IndexOf("translate", StringComparison.Ordinal) >= 0 ||
				 xpath.IndexOf("not(", StringComparison.Ordinal) >= 0);
			XmlLoadTiming.Add("XPath(" + (cached ? "cache/" : "native/") +
				(predicate ? "predicate" : "plain") + ")", __state);
			if (predicate && __result != null)
				__result = new CountingEnumerator(__result, cached ? "cache" : "native", xpath);
		}

		private sealed class CountingEnumerator : IEnumerator
		{
			private readonly IEnumerator _inner;
			private readonly string _tag;
			private readonly string _xpath;
			private readonly long _start;
			private int _count;
			private bool _finished;

			internal CountingEnumerator(IEnumerator inner, string tag, string xpath)
			{
				_inner = inner;
				_tag = tag;
				_xpath = xpath;
				_start = Stopwatch.GetTimestamp();
			}

			public object Current => _inner.Current;

			public bool MoveNext()
			{
				if (_inner.MoveNext())
				{
					_count++;
					return true;
				}
				Finish();
				return false;
			}

			public void Reset()
			{
				_inner.Reset();
			}

			private void Finish()
			{
				if (_finished)
					return;
				_finished = true;
				var text = _xpath != null ? _xpath.Replace('\n', ' ').Replace('\t', ' ') : "null";
				if (text.Length > 140)
					text = text.Substring(0, 140) + "...";
				YaOptMod.Log("[YaOpt-Timing] xpath scan " + _tag + ": results=" + _count + ", " +
					XmlLoadTiming.ElapsedMs(_start).ToString("F1") + " ms xpath=" + text);
			}
		}
	}

	// SelectNodes builds a lazily evaluated list, so the evaluation time lands in the caller's
	// enumeration. Timing the call itself still separates expression compilation from node walking.
	[EarlyPatch]
	[HarmonyPatch]
	internal static class TempTiming_SelectNodes
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(XmlNode), nameof(XmlNode.SelectNodes),
				new[] { typeof(string) });
			yield return AccessTools.Method(typeof(XmlNode), nameof(XmlNode.SelectNodes),
				new[] { typeof(string), typeof(XmlNamespaceManager) });
		}

		static void Prefix(out long __state)
		{
			__state = Stopwatch.GetTimestamp();
		}

		static void Postfix(string xpath, long __state)
		{
			var ms = XmlLoadTiming.ElapsedMs(__state);
			if (ms < 20)
				return;
			var text = xpath != null ? xpath.Replace('\n', ' ').Replace('\t', ' ') : "null";
			if (text.Length > 140)
				text = text.Substring(0, 140) + "...";
			YaOptMod.Log("[YaOpt-Timing] SelectNodes setup " + ms.ToString("F1") + " ms xpath=" + text);
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
			XmlLoadTiming.DumpGc("XML def load");
			YaOptMod.Log("[YaOpt-Timing] after ParseAndProcessXML: " + XPathCachePeek.Describe(null));
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
			TempTiming_ThinkKeyVerify.Dump();
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

	// Compares the predicate-path evaluator against the native engine on a small synthetic document
	// that mirrors the bow rotation patch. Runs once, before the real XPath cache is built.
	[EarlyPatch]
	[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML))]
	internal static class TempTiming_XPathPredicateSelfTest
	{
		private static bool _ran;

		static void Prefix()
		{
			if (_ran)
				return;
			_ran = true;
			try
			{
				Run();
			}
			catch (Exception ex)
			{
				YaOptMod.Warning("[YaOpt-Test] predicate path self-test failed: " + ex);
			}
		}

		private static void Run()
		{
			const string xml =
				"<Defs>" +
				"<ThingDef><defName>A</defName><label>Short bow</label><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>B</defName><label>Cross bow</label><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>C</defName><label>Bow</label><apparel /><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>D</defName><label>Bow</label><rotateInShelves>true</rotateInShelves><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>E</defName><label>Bow</label><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>F</defName><label>Bow</label><verbs><li /></verbs><equipmentType>Secondary</equipmentType></ThingDef>" +
				"<ThingDef><defName>G</defName><label>long BOW</label><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"<ThingDef><defName>H</defName><label>Crossbow</label><verbs><li /></verbs><equipmentType>Primary</equipmentType></ThingDef>" +
				"</Defs>";

			const string xpath = "Defs/ThingDef\n\t[not(rotateInShelves)]\n\t[not(apparel)]\n\t[verbs/li]\n\t" +
				"[equipmentType=\"Primary\"]\n\t[contains(translate(label,\"bow\",\"BOW\"), \"BOW\")]\n\t" +
				"[not(contains(translate(label,\"cros\",\"CROS\"), \"CROSS\"))]";

			var document = new XmlDocument();
			document.LoadXml(xml);

			var native = new List<XmlNode>();
			foreach (XmlNode node in document.SelectNodes(xpath))
				native.Add(node);

			XPathReducer.CreateCache(document);
			// equal alone proves nothing: a parse failure silently falls back to the native engine and
			// still returns the same nodes. Invoke the fast evaluator directly to see if it accepted
			// this xpath at all.
			var parse = AccessTools.Method(typeof(XPathReducer), "TryRunDefPredicatePath");
			var accepted = parse != null && parse.Invoke(null, new object[] { "/" + xpath }) is bool ok && ok;
			var ours = XPathReducer.GetXmlNodes(document, xpath);
			XPathReducer.ClearCache();

			var equal = ours.Length == native.Count;
			if (equal)
			{
				for (var i = 0; i < ours.Length; i++)
				{
					if (!ReferenceEquals(ours[i], native[i]))
					{
						equal = false;
						break;
					}
				}
			}

			var option = YaOptGlobal.Settings == null
				? "null"
				: YaOptGlobal.Settings.OptFastDefPredicateXPath.Enabled.ToString();
			YaOptMod.Log("[YaOpt-Test] predicate path self-test: ours=" + ours.Length + " native=" +
				native.Count + " equal=" + equal + " fastAccepted=" + accepted + " option=" + option);
		}
	}
}
#endif
