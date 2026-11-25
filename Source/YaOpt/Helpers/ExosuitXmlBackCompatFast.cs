using System;
using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	///     Replaces Exosuit's <c>Exosuit.XmlBackCompat</c> patch operation with a value-only walk.
	/// </summary>
	/// <remarks>
	///     The original recursively visits every node and rebuilds its class-name dictionary
	///     (through a LINQ property) for every attribute and text node. On a heavily modded load
	///     that document is tens of MB, so the operation alone can take seconds; the replacement
	///     builds the dictionary once and only visits nodes whose value can actually match.
	/// </remarks>
	internal static class ExosuitXmlBackCompatFast
	{
		private const string TypeName = "Exosuit.XmlBackCompat";
		private const string AssemblyName = "Exosuit";
		private const string TargetsField = "targetClassList";
		private const string ReplacementsField = "classReplacedList";

		internal static bool Install(Harmony harmony)
		{
			var patched = 0;
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (var i = 0; i < assemblies.Length; i++)
			{
				// The class lives in Exosuit.dll; skip the other ~600 mod assemblies entirely so the
				// install pass stays cheap and cannot touch an unrelated type with the same full name.
				if (!string.Equals(assemblies[i].GetName().Name, AssemblyName, StringComparison.Ordinal))
					continue;

				Type type;
				try
				{
					// Both the 1.6 and 1.7 folders ship an Exosuit.dll; patch whichever one(s) loaded.
					type = assemblies[i].GetType(TypeName, throwOnError: false);
				}
				catch (Exception)
				{
					continue;
				}

				if (type == null)
					continue;

				var method = AccessTools.Method(type, "ApplyWorker");
				if (method == null)
				{
					YaOptMod.Warning($"{TypeName}.ApplyWorker not found; the compat patch is inactive.");
					continue;
				}

				try
				{
					harmony.Patch(method,
						prefix: new HarmonyMethod(typeof(ExosuitXmlBackCompatFast), nameof(Prefix)));
					patched++;
				}
				catch (Exception exception)
				{
					YaOptMod.Warning(
						$"Could not patch {TypeName}.ApplyWorker; the compat patch is inactive:\n{exception}");
				}
			}

			if (patched > 0)
			{
				YaOptMod.Log(
					$"Exosuit.XmlBackCompat patched ({patched} assembly/assemblies); the full-document " +
					"class rename now visits values only.");
			}

			return true;
		}

		private static bool Prefix(object __instance, XmlDocument xml, ref bool __result)
		{
			if (!YaOptGlobal.Settings.OptExosuitXmlFast.Enabled)
				return true;

			__result = true;

			var element = xml?.DocumentElement;
			if (element == null)
				return false;

			var type = __instance.GetType();
			var targets = AccessTools.Field(type, TargetsField)?.GetValue(__instance) as List<string>;
			var replacements = AccessTools.Field(type, ReplacementsField)?.GetValue(__instance) as List<string>;
			if (targets == null || replacements == null)
			{
				// Fields changed in a future Exosuit version: fall back to the original implementation.
				return true;
			}

			var count = Math.Min(targets.Count, replacements.Count);
			if (count == 0)
				return false;

			var map = new Dictionary<string, string>(count, StringComparer.Ordinal);
			for (var i = 0; i < count; i++)
			{
				var key = targets[i];
				if (!string.IsNullOrEmpty(key))
					map[key] = replacements[i];
			}

			Walk(element, map);
			return false;
		}

		private static void Walk(XmlNode node, Dictionary<string, string> map)
		{
			if (node.NodeType == XmlNodeType.Element)
			{
				var attributes = node.Attributes;
				if (attributes != null)
				{
					for (var i = 0; i < attributes.Count; i++)
					{
						var attribute = attributes[i];
						if (map.TryGetValue(attribute.Value, out var replacement))
							attribute.Value = replacement;
					}
				}
			}
			else if (node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA)
			{
				var value = node.Value;
				if (value != null && map.TryGetValue(value, out var replacement))
					node.Value = replacement;
			}

			for (var child = node.FirstChild; child != null; child = child.NextSibling)
				Walk(child, map);
		}
	}
}