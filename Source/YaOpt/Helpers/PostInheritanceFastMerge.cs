using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces the body of PostInheritanceOperation's <c>ApplyXmlInheritanceOnDocument</c> with a
	/// single rebuild of the unified document's top-level child list.
	/// </summary>
	/// <remarks>
	/// The original walks the resolved-node table and swaps each inherited def in place with
	/// <c>InsertAfter(resolved, original)</c> + <c>RemoveChild(original)</c>. On this runtime
	/// <c>XmlNode.InsertAfter</c> asks <c>refChild.PreviousSibling</c>, and <c>XmlNode.RemoveChild</c>
	/// asks it again for the middle and last branches; <c>PreviousSibling</c> walks the parent's
	/// singly linked child list from the first child, so the whole pass is O(n²) in the number of
	/// top-level defs (about 11 s with 57k defs). Removing every child (FirstChild is O(1)) and
	/// appending the final order is O(n) and yields the same child sequence.
	///
	/// A body replacement is used instead of a prefix that returns false: other prefixes (load
	/// timers, for example) still run around it and can still skip the whole method, exactly as they
	/// would skip the original. Only a foreign transpiler, which would be discarded along with the
	/// original instructions, disables the replacement.
	///
	/// Two cases fall back to the original algorithm: a document with node-change event handlers
	/// (the original fires one Insert and one Remove per replaced def, a bulk rebuild fires a
	/// different sequence), and a resolved node that is already attached to the document (the
	/// original moves it instead of copying it).
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFastPostInheritanceMerge" />
	internal static class PostInheritanceFastMerge
	{
		private const string PatchQueueTypeName = "PostInheritanceOperation.PatchQueue";
		private const string TargetMethodName = "ApplyXmlInheritanceOnDocument";
		private const string ResolvedNodesFieldName = "resolvedNodes";
		private const string InheritanceNodeTypeName = "XmlInheritanceNode";
		private const string ResolvedNodeFieldName = "resolvedXmlNode";

		// Private XmlDocument delegates that receive node-change events. Vanilla never attaches them.
		private static readonly string[] EventFieldNames =
		{
			"onNodeInsertingDelegate", "onNodeInsertedDelegate",
			"onNodeRemovingDelegate", "onNodeRemovedDelegate",
			"onNodeChangingDelegate", "onNodeChangedDelegate"
		};

		private static FieldInfo _resolvedNodesField;
		private static AccessTools.FieldRef<object, XmlNode> _resolvedNode;
		private static FieldInfo[] _eventFields;
		private static bool _installed;

		internal static bool Install(Harmony harmony)
		{
			if (_installed)
				return true;
			_installed = true;

			try
			{
				return InstallCore(harmony);
			}
			catch (Exception exception)
			{
				YaOptMod.Warning("PostInheritanceOperation's fast merge could not be installed:\n" + exception);
				return true;
			}
		}

		private static bool InstallCore(Harmony harmony)
		{
			var queueType = AccessTools.TypeByName(PatchQueueTypeName);
			if (queueType == null)
				return true;

			var target = AccessTools.DeclaredMethod(queueType, TargetMethodName, new[] { typeof(XmlDocument) });
			if (target == null)
			{
				YaOptMod.Warning($"{PatchQueueTypeName}.{TargetMethodName} not found; the fast merge is inactive.");
				return true;
			}

			if (!HasExpectedShape(target))
			{
				YaOptMod.Warning(
					$"{PatchQueueTypeName}.{TargetMethodName} no longer has the expected insert/remove body; " +
					"the fast merge is inactive.");
				return true;
			}

			_resolvedNodesField = AccessTools.Field(typeof(XmlInheritance), ResolvedNodesFieldName);
			var nodeType = AccessTools.Inner(typeof(XmlInheritance), InheritanceNodeTypeName);
			var resolvedNodeField = nodeType == null ? null : AccessTools.Field(nodeType, ResolvedNodeFieldName);
			if (_resolvedNodesField == null || !typeof(IDictionary).IsAssignableFrom(_resolvedNodesField.FieldType) ||
				resolvedNodeField == null || resolvedNodeField.FieldType != typeof(XmlNode))
			{
				YaOptMod.Warning(
					"XmlInheritance's resolved node table has a different shape; the fast merge is inactive.");
				return true;
			}

			_eventFields = new FieldInfo[EventFieldNames.Length];
			for (var i = 0; i < EventFieldNames.Length; i++)
			{
				_eventFields[i] = AccessTools.Field(typeof(XmlDocument), "_" + EventFieldNames[i]) ??
					AccessTools.Field(typeof(XmlDocument), EventFieldNames[i]);
				if (_eventFields[i] == null)
				{
					YaOptMod.Warning(
						"XmlDocument's node-change delegate fields were not found; the fast merge is inactive.");
					return true;
				}
			}

			var conflicts = new List<string>();
			PatchConflictGuard.CollectForeignTranspilers(target, conflicts);
			if (conflicts.Count > 0)
			{
				YaOptMod.Warning(
					$"Another mod transpiles {PatchQueueTypeName}.{TargetMethodName}; the fast merge is inactive " +
					"to avoid discarding it:\n" + string.Join("\n", conflicts));
				return true;
			}

			_resolvedNode = AccessTools.FieldRefAccess<XmlNode>(nodeType, ResolvedNodeFieldName);

			harmony.Patch(target,
				transpiler: new HarmonyMethod(typeof(PostInheritanceFastMerge), nameof(Transpiler)));
			YaOptMod.Log(
				"PostInheritanceOperation's post-inheritance merge now rebuilds the def list in one pass.");
			return true;
		}

		/// <summary>
		/// Scans the target IL for the two DOM calls the replacement assumes. A rewrite that merges
		/// with different APIs leaves the option inactive rather than applying a wrong result.
		/// </summary>
		private static bool HasExpectedShape(MethodInfo method)
		{
			byte[] il;
			try
			{
				il = method.GetMethodBody()?.GetILAsByteArray();
			}
			catch (Exception)
			{
				return false;
			}

			if (il == null)
				return false;

			var insertAfter = false;
			var removeChild = false;
			for (var i = 0; i + 4 < il.Length; i++)
			{
				// call (0x28) / callvirt (0x6F); a little-endian metadata token follows.
				if (il[i] != 0x28 && il[i] != 0x6F)
					continue;

				MethodBase called;
				try
				{
					called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
				}
				catch (Exception)
				{
					continue;
				}

				if (called == null || called.DeclaringType != typeof(XmlNode))
					continue;
				if (called.Name == nameof(XmlNode.InsertAfter))
					insertAfter = true;
				else if (called.Name == nameof(XmlNode.RemoveChild))
					removeChild = true;
			}

			return insertAfter && removeChild;
		}

		/// <summary>
		/// Discards the original body (there are no branches or locals we need) and calls
		/// <see cref="Apply"/> with the same <see cref="XmlDocument"/> argument.
		/// </summary>
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			yield return new CodeInstruction(OpCodes.Ldarg_0);
			yield return new CodeInstruction(OpCodes.Call,
				AccessTools.Method(typeof(PostInheritanceFastMerge), nameof(Apply)));
			yield return new CodeInstruction(OpCodes.Ret);
		}

		private static void Apply(XmlDocument xmlDocument)
		{
			if (xmlDocument == null)
				return;
			var documentElement = xmlDocument.DocumentElement;
			if (documentElement == null)
				return;

			if (!(_resolvedNodesField.GetValue(null) is IDictionary resolvedNodes))
				return;

			var childNodes = documentElement.ChildNodes;
			var snapshot = new XmlNode[childNodes.Count];
			var count = 0;
			foreach (XmlNode child in childNodes)
			{
				if (count < snapshot.Length)
					snapshot[count++] = child;
			}

			if (!YaOptGlobal.Settings.OptFastPostInheritanceMerge.Enabled ||
				HasDocumentEvents(xmlDocument) ||
				HasAttachedResolvedNode(snapshot, count, xmlDocument, resolvedNodes))
			{
				ApplyOriginal(documentElement, xmlDocument, resolvedNodes, snapshot, count);
				return;
			}

			var ordered = new List<XmlNode>(count);
			var replaced = 0;
			for (var i = 0; i < count; i++)
			{
				var child = snapshot[i];
				if (child is XmlElement element && resolvedNodes.Contains(element))
				{
					var inheritanceNode = resolvedNodes[element];
					var resolved = ReadResolved(inheritanceNode);
					if (resolved != null && resolved != element && !resolvedNodes.Contains(resolved))
					{
						resolvedNodes.Add(resolved, inheritanceNode);
						if (resolved.OwnerDocument != xmlDocument)
						{
							resolved = xmlDocument.ImportNode(resolved, true);
							_resolvedNode(inheritanceNode) = resolved;
						}

						child = resolved;
						replaced++;
					}
				}

				ordered.Add(child);
			}

			// Nothing resolves to a different node, so the original would not change the document.
			if (replaced == 0)
				return;

			while (documentElement.FirstChild != null)
				documentElement.RemoveChild(documentElement.FirstChild);
			for (var i = 0; i < ordered.Count; i++)
				documentElement.AppendChild(ordered[i]);
		}

		/// <summary>
		/// The original algorithm, kept for the fallback cases and for a runtime-disabled option.
		/// </summary>
		private static void ApplyOriginal(XmlElement documentElement, XmlDocument xmlDocument,
			IDictionary resolvedNodes, XmlNode[] snapshot, int count)
		{
			for (var i = 0; i < count; i++)
			{
				if (!(snapshot[i] is XmlElement element) || !resolvedNodes.Contains(element))
					continue;

				var inheritanceNode = resolvedNodes[element];
				var resolved = ReadResolved(inheritanceNode);
				if (resolved == null || resolved == element || resolvedNodes.Contains(resolved))
					continue;

				resolvedNodes.Add(resolved, inheritanceNode);
				if (resolved.OwnerDocument != xmlDocument)
				{
					resolved = xmlDocument.ImportNode(resolved, true);
					_resolvedNode(inheritanceNode) = resolved;
				}

				documentElement.InsertAfter(resolved, element);
				documentElement.RemoveChild(element);
			}
		}

		private static bool HasDocumentEvents(XmlDocument xmlDocument)
		{
			for (var i = 0; i < _eventFields.Length; i++)
			{
				if (_eventFields[i].GetValue(xmlDocument) != null)
					return true;
			}

			return false;
		}

		/// <summary>
		/// The original moves a resolved node that is already attached to this document
		/// (<c>InsertAfter</c> detaches it from its old parent first). A rebuild cannot replay a
		/// move, so that case goes back to the original algorithm.
		/// </summary>
		private static bool HasAttachedResolvedNode(XmlNode[] snapshot, int count, XmlDocument xmlDocument,
			IDictionary resolvedNodes)
		{
			for (var i = 0; i < count; i++)
			{
				if (!(snapshot[i] is XmlElement element) || !resolvedNodes.Contains(element))
					continue;

				var resolved = ReadResolved(resolvedNodes[element]);
				if (resolved != null && resolved != element && resolved.OwnerDocument == xmlDocument &&
					resolved.ParentNode != null && !resolvedNodes.Contains(resolved))
				{
					return true;
				}
			}

			return false;
		}

		private static XmlNode ReadResolved(object inheritanceNode)
		{
			if (inheritanceNode == null)
				return null;

			return _resolvedNode(inheritanceNode);
		}
	}
}
