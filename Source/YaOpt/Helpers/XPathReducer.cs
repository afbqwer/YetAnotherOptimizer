using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml;

namespace YaOpt.Helpers
{
	internal static class XPathReducer
	{
		private const string IDENTITY = @"[a-zA-Z_][\w\-\.]";
		private const string DEF_NAME = "defName";
		private const string ANY_SPACE = @"\s*";
		private const string EQUALITY = ANY_SPACE + "=" + ANY_SPACE;

		private static Dictionary<string, Dictionary<string, XmlElement>> _xmlCache;
		private static XmlElement _xmlDefs;
		private static Queue<(XmlElement, int)> _tmpQueue;
		private static NodeEnumerator _singletonEnumerator;
		private static bool _available;

		// The predicate name may start with "@" for an attribute ([@Class="..."]) or not for a child
		// element ([defName="..."]). Only one predicate per step and double-quoted values are handled.
		private static readonly Regex _xPathRegex = new Regex(
			"^" +
			"(?:/(" + IDENTITY + "*" +
			"(?:" + @"\[" + ANY_SPACE + "@?" + IDENTITY + "+" + EQUALITY + @"""[^""]*""" + ANY_SPACE + @"\])?" +
			"))+$",
			RegexOptions.Compiled);

		private static readonly Regex _queryRegex = new Regex(
			"^(?:(" + IDENTITY + @"*)\[" + ANY_SPACE + "(@?" + IDENTITY + "+)" + EQUALITY +
			@"""([^""]*)""" + ANY_SPACE + @"\])$",
			RegexOptions.Compiled);

		// One [name="value"] or [@name="value"] predicate inside a descendant path.
		private static readonly Regex _predicateRegex = new Regex(
			"^(@?" + IDENTITY + "*)" + EQUALITY + @"""([^""]*)""$",
			RegexOptions.Compiled);

		private class NodeEnumerator : IEnumerator
		{
			public bool MoveNext()
			{
				return _tmpQueue.Count > 0;
			}

			public void Reset()
			{
			}

			public object Current => _tmpQueue.Dequeue().Item1;
		}

		public static void CreateCache(XmlDocument xml)
		{
			_xmlDefs = xml?["Defs"];
			_available = false;
			_xmlCache = null;
			_tmpQueue = null;
			_singletonEnumerator = null;
			if (_xmlDefs == null)
				return;

			var nameSet = new HashSet<(string, string)>();
			_xmlCache = new Dictionary<string, Dictionary<string, XmlElement>>();
			_tmpQueue = new Queue<(XmlElement, int)>();
			_singletonEnumerator = new NodeEnumerator();
			foreach (object obj in _xmlDefs.ChildNodes)
			{
				if (!(obj is XmlElement def))
					continue;
				var defKind = def.Name;
				if (!_xmlCache.TryGetValue(defKind, out var defCacheOfKind))
				{
					defCacheOfKind = new Dictionary<string, XmlElement>();
					_xmlCache[defKind] = defCacheOfKind;
				}
				var defNameNode = def["defName"];
				if (defNameNode != null)
				{
					var name = defNameNode.InnerText;
					// It will only cache the defs which have unique name.
					var nameTuple = (defKind, name);
					if (nameSet.Add(nameTuple))
						defCacheOfKind[name] = def;
					else
						defCacheOfKind.Remove(name);
				}
			}
			_available = true;
		}

		public static void ClearCache()
		{
			_available = false;
			_xmlCache = null;
			_xmlDefs = null;
			_tmpQueue = null;
			_singletonEnumerator = null;
			GC.Collect(0, GCCollectionMode.Optimized, false);
		}

		[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
		[SuppressMessage("ReSharper", "NotDisposedResourceIsReturned")]
		public static IEnumerator GetXmlEnumerator(XmlDocument xml, string xpath)
		{
			if (!_available)
				return xml.SelectNodes(xpath).GetEnumerator();

			if (xpath.StartsWith("Defs/", StringComparison.Ordinal))
				xpath = "/" + xpath;

			// Descendant steps ("//") are evaluated by a document-order walk instead of the forward
			// child-step path below. A native XPath descendant scan over the unified document costs
			// 100+ ms; the walk is the same query without the navigator overhead.
			if (xpath.IndexOf("//", StringComparison.Ordinal) >= 0)
				return GetDescendantEnumerator(xml, xpath);

			var shouldFallback = true;
			try
			{
				_tmpQueue.Clear();
				var matches = _xPathRegex.Match(xpath);
				if (matches.Success)
				{
					shouldFallback = false;
					var captures = matches.Groups[1].Captures;
					var count = captures.Count;
					if (captures[0].Value == "Defs")
					{
						_tmpQueue.Enqueue((_xmlDefs, 0));

						for (var i = 1; i < count; i++)
						{
							if (_tmpQueue.Count == 0)
							{
								break;
							}
							var capture = captures[i].Value;
							var queryMatches = _queryRegex.Match(capture);
							if (queryMatches.Success)
							{
								var nodeName = queryMatches.Groups[1].Value;
								var queryName = queryMatches.Groups[2].Value;
								var queryValue = queryMatches.Groups[3].Value;
								if (i == 1 && queryName == "defName" &&
									_xmlCache.TryGetValue(nodeName, out var dict) &&
									dict.TryGetValue(queryValue, out var nextNode) &&
									nextNode.ParentNode != null)
								{
									_tmpQueue.Dequeue();
									_tmpQueue.Enqueue((nextNode, 1));
								}
								else
								{
									while (_tmpQueue.Count > 0 && _tmpQueue.Peek().Item2 == i - 1)
									{
										var parentNode = _tmpQueue.Dequeue().Item1;
										foreach (object obj in parentNode.ChildNodes)
										{
											if (!(obj is XmlElement childNode))
												continue;

											if (childNode.Name == nodeName &&
												MatchesPredicate(childNode, queryName, queryValue))
											{
												_tmpQueue.Enqueue((childNode, i));
											}
										}
									}
								}
							}
							else
							{
								while (_tmpQueue.Count > 0 && _tmpQueue.Peek().Item2 == i - 1)
								{
									var parentNode = _tmpQueue.Dequeue().Item1;
									foreach (object obj in parentNode.ChildNodes)
									{
										if (!(obj is XmlElement childNode))
											continue;

										if (childNode.Name == capture)
										{
											_tmpQueue.Enqueue((childNode, i));
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				shouldFallback = true;
				_tmpQueue.Clear();
				YaOptMod.Error("Error when optimized patching. Fallback to vanilla method. " +
							   $"Exception: {ex}");
			}

			if (!shouldFallback)
			{
				_singletonEnumerator.Reset();
				return _singletonEnumerator;
			}
			//YaOptMod.Warning($"Fallback xpath: {xpath}");
			return xml.SelectNodes(xpath).GetEnumerator();
		}

		public static XmlNode GetXmlFirstNode(XmlDocument xml, string xpath)
		{
			var enumerator = GetXmlEnumerator(xml, xpath);
			if (enumerator.MoveNext())
				return enumerator.Current as XmlNode;
			return null;
		}

		/// <summary>
		/// Materializes the same nodes as <c>xml.SelectNodes(xpath).Cast&lt;XmlNode&gt;().ToArray()</c>.
		/// </summary>
		/// <remarks>
		/// Replace, Remove and SetName delete or rename the matched nodes, so they snapshot the
		/// result before iterating. The shared scratch enumerator is drained immediately here, which
		/// keeps the returned array independent of later lookups.
		/// </remarks>
		public static XmlNode[] GetXmlNodes(XmlDocument xml, string xpath)
		{
			var enumerator = GetXmlEnumerator(xml, xpath);
			var nodes = new List<XmlNode>();
			while (enumerator.MoveNext())
			{
				if (enumerator.Current is XmlNode node)
					nodes.Add(node);
			}
			return nodes.ToArray();
		}

		private static IEnumerator GetDescendantEnumerator(XmlDocument xml, string xpath)
		{
			try
			{
				_tmpQueue.Clear();
				if (TryRunDescendantPath(xml, xpath))
				{
					_singletonEnumerator.Reset();
					return _singletonEnumerator;
				}
			}
			catch (Exception ex)
			{
				_tmpQueue.Clear();
				YaOptMod.Error("Error when optimized descendant patching. Fallback to vanilla method. " +
							   $"Exception: {ex}");
			}
			return xml.SelectNodes(xpath).GetEnumerator();
		}

		private readonly struct Step
		{
			internal readonly bool Descendant;
			internal readonly string Name;
			internal readonly string QueryName;
			internal readonly string QueryValue;

			internal Step(bool descendant, string name, string queryName, string queryValue)
			{
				Descendant = descendant;
				Name = name;
				QueryName = queryName;
				QueryValue = queryValue;
			}
		}

		/// <summary>
		/// Parses a path made of child ("/") and descendant ("//") steps, each with at most one
		/// equality predicate. Anything else (positions, functions, wildcards, unions) is rejected so
		/// the caller falls back to the native XPath engine.
		/// </summary>
		private static bool TryParsePath(string xpath, out List<Step> steps)
		{
			steps = new List<Step>(4);
			var i = 0;
			while (i < xpath.Length)
			{
				if (xpath[i] != '/')
					return false;
				var descendant = i + 1 < xpath.Length && xpath[i + 1] == '/';
				i += descendant ? 2 : 1;

				if (i >= xpath.Length || !IsNameStart(xpath[i]))
					return false;
				var nameStart = i++;
				while (i < xpath.Length && IsNameChar(xpath[i]))
					i++;
				var name = xpath.Substring(nameStart, i - nameStart);

				string queryName = null;
				string queryValue = null;
				if (i < xpath.Length && xpath[i] == '[')
				{
					var start = ++i;
					var inQuote = false;
					while (i < xpath.Length)
					{
						var ch = xpath[i];
						if (ch == '"')
							inQuote = !inQuote;
						else if (ch == ']' && !inQuote)
							break;
						i++;
					}
					if (i >= xpath.Length)
						return false;

					var predicate = _predicateRegex.Match(xpath.Substring(start, i - start).Trim());
					if (!predicate.Success)
						return false;
					queryName = predicate.Groups[1].Value;
					queryValue = predicate.Groups[2].Value;
					i++;
				}

				steps.Add(new Step(descendant, name, queryName, queryValue));
			}
			return steps.Count > 0;
		}

		/// <summary>
		/// Evaluates the parsed path against the live document. Child steps keep document order;
		/// descendant steps walk each context subtree in document order. With several overlapping
		/// contexts the concatenation can repeat a node, so duplicates are dropped keeping the first
		/// occurrence, which is still document order.
		/// </summary>
		private static bool TryRunDescendantPath(XmlDocument xml, string xpath)
		{
			if (!TryParsePath(xpath, out var steps))
				return false;

			var first = steps[0];
			List<XmlElement> current;
			if (first.Descendant)
			{
				current = new List<XmlElement>();
				Descend(xml, first, current);
			}
			else
			{
				if (!string.Equals(first.Name, "Defs", StringComparison.Ordinal) || _xmlDefs == null)
					return false;
				if (first.QueryName != null && !MatchesPredicate(_xmlDefs, first.QueryName, first.QueryValue))
					return true;
				current = new List<XmlElement>(1) { _xmlDefs };
			}

			for (var s = 1; s < steps.Count; s++)
			{
				var step = steps[s];
				var next = new List<XmlElement>();
				if (step.Descendant)
				{
					for (var i = 0; i < current.Count; i++)
						Descend(current[i], step, next);
					if (current.Count > 1)
						Dedupe(next);
				}
				else
				{
					for (var i = 0; i < current.Count; i++)
					{
						var parent = current[i];
						// Top-level defs are unique by name; the cache turns a 57k-child scan into a
						// dictionary lookup. The parent check keeps deeper "defName" elements exact.
						if (step.QueryName == "defName" && _xmlCache.TryGetValue(step.Name, out var byName) &&
							byName.TryGetValue(step.QueryValue, out var cached) && cached.ParentNode == parent)
						{
							next.Add(cached);
							continue;
						}

						foreach (object obj in parent.ChildNodes)
						{
							if (obj is XmlElement child && MatchesStep(child, step))
								next.Add(child);
						}
					}
				}

				current = next;
				if (current.Count == 0)
					break;
			}

			for (var i = 0; i < current.Count; i++)
				_tmpQueue.Enqueue((current[i], 0));
			return true;
		}

		private static void Descend(XmlNode node, Step step, List<XmlElement> result)
		{
			for (var child = node.FirstChild; child != null; child = child.NextSibling)
			{
				if (!(child is XmlElement element))
					continue;
				if (MatchesStep(element, step))
					result.Add(element);
				Descend(element, step, result);
			}
		}

		private static bool MatchesStep(XmlElement element, Step step)
		{
			return element.Name == step.Name &&
				   (step.QueryName == null || MatchesPredicate(element, step.QueryName, step.QueryValue));
		}

		private static void Dedupe(List<XmlElement> nodes)
		{
			var seen = new HashSet<XmlElement>();
			var write = 0;
			for (var read = 0; read < nodes.Count; read++)
			{
				if (seen.Add(nodes[read]))
					nodes[write++] = nodes[read];
			}
			if (write < nodes.Count)
				nodes.RemoveRange(write, nodes.Count - write);
		}

		private static bool IsNameStart(char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
		}

		private static bool IsNameChar(char c)
		{
			return IsNameStart(c) || (c >= '0' && c <= '9') || c == '-' || c == '.';
		}

		/// <summary>
		/// Matches one <c>[name="value"]</c> predicate. A leading "@" means an attribute, otherwise a
		/// child element's text must equal the value, which mirrors XPath's string-value comparison
		/// closely enough for the def fields these patches use.
		/// </summary>
		private static bool MatchesPredicate(XmlElement element, string queryName, string queryValue)
		{
			if (queryName[0] == '@')
			{
				var attribute = element.GetAttributeNode(queryName.Substring(1));
				return attribute != null && attribute.Value == queryValue;
			}

			foreach (object obj in element.ChildNodes)
			{
				if (obj is XmlElement child && child.Name == queryName && child.InnerText == queryValue)
					return true;
			}
			return false;
		}
	}
}