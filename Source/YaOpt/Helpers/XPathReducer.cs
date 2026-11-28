using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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
		private static List<XmlElement> _tmpDefs;
		private static List<XmlElement> _tmpDefsNext;
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
			_tmpDefs = null;
			_tmpDefsNext = null;
			_singletonEnumerator = null;
			if (_xmlDefs == null)
				return;

			var nameSet = new HashSet<(string, string)>();
			_xmlCache = new Dictionary<string, Dictionary<string, XmlElement>>();
			_tmpQueue = new Queue<(XmlElement, int)>();
			_tmpDefs = new List<XmlElement>();
			_tmpDefsNext = new List<XmlElement>();
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
			_tmpDefs = null;
			_tmpDefsNext = null;
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

			var settings = YaOptGlobal.Settings;
			var fastSimple = settings == null || settings.OptFastPatchOperation.Enabled;
			var fastPredicates = settings != null && settings.OptFastDefPredicateXPath.Enabled;

			// Descendant steps ("//") are evaluated by a document-order walk instead of the forward
			// child-step path below. A native XPath descendant scan over the unified document costs
			// 100+ ms; the walk is the same query without the navigator overhead.
			if (fastSimple && xpath.IndexOf("//", StringComparison.Ordinal) >= 0)
				return GetDescendantEnumerator(xml, xpath);

			var shouldFallback = true;
			try
			{
				_tmpQueue.Clear();
				var matches = fastSimple ? _xPathRegex.Match(xpath) : null;
				if (matches != null && matches.Success)
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
				// Predicate-function paths ("not()", "contains(translate())", child paths) are rejected
				// by the regex above, but the native engine takes seconds over the unified document.
				// Evaluate the supported subset directly over the Defs children instead.
				if (shouldFallback && fastPredicates && TryRunDefPredicatePath(xpath))
					shouldFallback = false;
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

		/// <summary>
		/// Evaluates the XPath 1.0 subset <c>/Defs/DefType[predicate...]/child/.../leaf[predicate...]</c>
		/// that the simple regex rejects. Supported predicates: child existence/absence, relative child
		/// paths, <c>@attr</c>/child equality and <c>contains(translate(...))</c>, optionally negated.
		/// Any other shape returns false so the native engine handles the query. The native engine is
		/// pathologically slow on these predicates after inheritance, because it walks the children of
		/// every resolved def (tens of thousands of nodes whose bodies are fully merged).
		/// </summary>
		private static bool TryRunDefPredicatePath(string xpath)
		{
			var position = 0;
			if (!TryConsumeChar(xpath, ref position, '/'))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!string.Equals(ReadName(xpath, ref position), "Defs", StringComparison.Ordinal))
				return false;
			if (!TryConsumeChar(xpath, ref position, '/'))
				return false;
			SkipWhitespace(xpath, ref position);
			var defName = ReadName(xpath, ref position);
			if (defName == null)
				return false;

			if (!TryParsePredicateList(xpath, ref position, out var predicates))
				return false;

			// Trailing child path, e.g. /building/fixedStorageSettings/filter[not(x)]. The result set is
			// the elements matched by the last step, so the query cannot just return the defs.
			List<DefPathStep> steps = null;
			while (true)
			{
				SkipWhitespace(xpath, ref position);
				if (position >= xpath.Length)
					break;
				if (!TryConsumeChar(xpath, ref position, '/'))
					return false;
				SkipWhitespace(xpath, ref position);
				var stepName = ReadName(xpath, ref position);
				if (stepName == null)
					return false;
				if (!TryParsePredicateList(xpath, ref position, out var stepPredicates))
					return false;
				if (steps == null)
					steps = new List<DefPathStep>(2);
				steps.Add(new DefPathStep(stepName, stepPredicates));
			}

			if ((predicates == null || predicates.Count == 0) && (steps == null || !HasAnyPredicate(steps)))
				return false;

			_tmpDefs.Clear();
			for (var child = _xmlDefs.FirstChild; child != null; child = child.NextSibling)
			{
				if (child is XmlElement element && element.Name == defName &&
					MatchesDefPredicates(element, predicates))
				{
					_tmpDefs.Add(element);
				}
			}

			if (steps != null)
			{
				for (var i = 0; i < steps.Count; i++)
				{
					var step = steps[i];
					_tmpDefsNext.Clear();
					for (var parent = 0; parent < _tmpDefs.Count; parent++)
					{
						for (var child = _tmpDefs[parent].FirstChild; child != null; child = child.NextSibling)
						{
							if (child is XmlElement element && element.Name == step.Name &&
								MatchesDefPredicates(element, step.Predicates))
							{
								_tmpDefsNext.Add(element);
							}
						}
					}
					var swap = _tmpDefs;
					_tmpDefs = _tmpDefsNext;
					_tmpDefsNext = swap;
				}
			}

			for (var i = 0; i < _tmpDefs.Count; i++)
				_tmpQueue.Enqueue((_tmpDefs[i], 0));
			return true;
		}

		private static bool HasAnyPredicate(List<DefPathStep> steps)
		{
			for (var i = 0; i < steps.Count; i++)
			{
				var predicates = steps[i].Predicates;
				if (predicates != null && predicates.Count > 0)
					return true;
			}
			return false;
		}

		private static bool TryParsePredicateList(string xpath, ref int position,
			out List<DefPredicate> predicates)
		{
			predicates = null;
			while (true)
			{
				SkipWhitespace(xpath, ref position);
				if (position >= xpath.Length || xpath[position] != '[')
					return true;
				position++;
				if (predicates == null)
					predicates = new List<DefPredicate>(4);
				if (!TryParsePredicate(xpath, ref position, predicates))
					return false;
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, ']'))
					return false;
			}
		}

		private readonly struct DefPathStep
		{
			internal readonly string Name;
			internal readonly List<DefPredicate> Predicates;

			internal DefPathStep(string name, List<DefPredicate> predicates)
			{
				Name = name;
				Predicates = predicates;
			}
		}

		private enum DefPredicateKind : byte
		{
			ChildExists,
			ChildAbsent,
			PathExists,
			PathAbsent,
			AttributeEquals,
			ChildEquals,
			ContainsTranslate,
			NotContainsTranslate
		}

		private readonly struct DefPredicate
		{
			internal readonly DefPredicateKind Kind;
			internal readonly string Name;
			internal readonly string Value;
			internal readonly string From;
			internal readonly string To;
			internal readonly string[] Path;

			internal DefPredicate(DefPredicateKind kind, string name, string value = null,
				string from = null, string to = null, string[] path = null)
			{
				Kind = kind;
				Name = name;
				Value = value;
				From = from;
				To = to;
				Path = path;
			}
		}

		private static bool TryParsePredicate(string xpath, ref int position, List<DefPredicate> result)
		{
			SkipWhitespace(xpath, ref position);
			if (TryConsumeWord(xpath, ref position, "not"))
			{
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, '('))
					return false;
				SkipWhitespace(xpath, ref position);
				if (TryConsumeWord(xpath, ref position, "contains"))
				{
					SkipWhitespace(xpath, ref position);
					if (!TryConsumeChar(xpath, ref position, '('))
						return false;
					if (!TryParseContainsTranslate(xpath, ref position, out var inner))
						return false;
					SkipWhitespace(xpath, ref position);
					// Closes contains(...).
					if (!TryConsumeChar(xpath, ref position, ')'))
						return false;
					SkipWhitespace(xpath, ref position);
					// Closes not(...).
					if (!TryConsumeChar(xpath, ref position, ')'))
						return false;
					result.Add(new DefPredicate(DefPredicateKind.NotContainsTranslate, inner.Name,
						inner.Value, inner.From, inner.To));
					return true;
				}

				SkipWhitespace(xpath, ref position);
				var first = ReadName(xpath, ref position);
				if (first == null)
					return false;
				var path = ParseChildPath(xpath, ref position, first);
				if (path == null)
					return false;
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, ')'))
					return false;
				if (path.Length == 1)
					result.Add(new DefPredicate(DefPredicateKind.ChildAbsent, path[0]));
				else
					result.Add(new DefPredicate(DefPredicateKind.PathAbsent, null, null, null, null, path));
				return true;
			}

			if (TryConsumeWord(xpath, ref position, "contains"))
			{
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, '('))
					return false;
				if (!TryParseContainsTranslate(xpath, ref position, out var predicate))
					return false;
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, ')'))
					return false;
				result.Add(predicate);
				return true;
			}

			if (position < xpath.Length && xpath[position] == '@')
			{
				position++;
				var attributeName = ReadName(xpath, ref position);
				if (attributeName == null)
					return false;
				SkipWhitespace(xpath, ref position);
				if (!TryConsumeChar(xpath, ref position, '='))
					return false;
				SkipWhitespace(xpath, ref position);
				if (!TryReadQuoted(xpath, ref position, out var attributeValue))
					return false;
				result.Add(new DefPredicate(DefPredicateKind.AttributeEquals, attributeName, attributeValue));
				return true;
			}

			var name = ReadName(xpath, ref position);
			if (name == null)
				return false;
			SkipWhitespace(xpath, ref position);
			if (TryConsumeChar(xpath, ref position, '='))
			{
				SkipWhitespace(xpath, ref position);
				if (!TryReadQuoted(xpath, ref position, out var value))
					return false;
				result.Add(new DefPredicate(DefPredicateKind.ChildEquals, name, value));
				return true;
			}

			if (position < xpath.Length && xpath[position] == '/')
			{
				var path = ParseChildPath(xpath, ref position, name);
				if (path == null)
					return false;
				result.Add(new DefPredicate(DefPredicateKind.PathExists, null, null, null, null, path));
				return true;
			}

			result.Add(new DefPredicate(DefPredicateKind.ChildExists, name));
			return true;
		}

		private static string[] ParseChildPath(string xpath, ref int position, string firstName)
		{
			var path = new List<string>(2) { firstName };
			while (position < xpath.Length && xpath[position] == '/')
			{
				position++;
				SkipWhitespace(xpath, ref position);
				var name = ReadName(xpath, ref position);
				if (name == null)
					return null;
				path.Add(name);
				SkipWhitespace(xpath, ref position);
			}
			return path.ToArray();
		}

		private static bool TryParseContainsTranslate(string xpath, ref int position,
			out DefPredicate predicate)
		{
			predicate = default;
			if (!TryConsumeWord(xpath, ref position, "translate"))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryConsumeChar(xpath, ref position, '('))
				return false;
			SkipWhitespace(xpath, ref position);
			var name = ReadName(xpath, ref position);
			if (name == null)
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryConsumeChar(xpath, ref position, ','))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryReadQuoted(xpath, ref position, out var from))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryConsumeChar(xpath, ref position, ','))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryReadQuoted(xpath, ref position, out var to))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryConsumeChar(xpath, ref position, ')'))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryConsumeChar(xpath, ref position, ','))
				return false;
			SkipWhitespace(xpath, ref position);
			if (!TryReadQuoted(xpath, ref position, out var value))
				return false;
			predicate = new DefPredicate(DefPredicateKind.ContainsTranslate, name, value, from, to);
			return true;
		}

		private static bool MatchesDefPredicates(XmlElement element, List<DefPredicate> predicates)
		{
			if (predicates == null || predicates.Count == 0)
				return true;
			for (var i = 0; i < predicates.Count; i++)
			{
				var predicate = predicates[i];
				bool matched;
				switch (predicate.Kind)
				{
					case DefPredicateKind.ChildExists:
						matched = FirstChildElement(element, predicate.Name) != null;
						break;
					case DefPredicateKind.ChildAbsent:
						matched = FirstChildElement(element, predicate.Name) == null;
						break;
					case DefPredicateKind.PathExists:
						matched = HasChildPath(element, predicate.Path);
						break;
					case DefPredicateKind.PathAbsent:
						matched = !HasChildPath(element, predicate.Path);
						break;
					case DefPredicateKind.AttributeEquals:
						var attribute = element.GetAttributeNode(predicate.Name);
						matched = attribute != null && attribute.Value == predicate.Value;
						break;
					case DefPredicateKind.ChildEquals:
						matched = AnyChildEquals(element, predicate.Name, predicate.Value);
						break;
					case DefPredicateKind.ContainsTranslate:
						matched = ContainsTranslated(element, predicate);
						break;
					case DefPredicateKind.NotContainsTranslate:
						matched = !ContainsTranslated(element, predicate);
						break;
					default:
						matched = false;
						break;
				}
				if (!matched)
					return false;
			}
			return true;
		}

		private static bool ContainsTranslated(XmlElement element, DefPredicate predicate)
		{
			var child = FirstChildElement(element, predicate.Name);
			var text = child != null ? child.InnerText : string.Empty;
			var translated = Translate(text, predicate.From, predicate.To);
			return translated.IndexOf(predicate.Value, StringComparison.Ordinal) >= 0;
		}

		private static bool HasChildPath(XmlNode node, string[] path)
		{
			for (var i = 0; i < path.Length; i++)
			{
				node = FirstChildElement(node, path[i]);
				if (node == null)
					return false;
			}
			return true;
		}

		private static XmlElement FirstChildElement(XmlNode node, string name)
		{
			for (var child = node.FirstChild; child != null; child = child.NextSibling)
			{
				if (child is XmlElement element && element.Name == name)
					return element;
			}
			return null;
		}

		// XPath equality against a node-set is existential, so any child with that name may match.
		private static bool AnyChildEquals(XmlNode node, string name, string value)
		{
			for (var child = node.FirstChild; child != null; child = child.NextSibling)
			{
				if (child is XmlElement element && element.Name == name && element.InnerText == value)
					return true;
			}
			return false;
		}

		private static string Translate(string value, string from, string to)
		{
			if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(from))
				return value ?? string.Empty;
			var builder = new StringBuilder(value.Length);
			for (var i = 0; i < value.Length; i++)
			{
				var index = from.IndexOf(value[i]);
				if (index < 0)
					builder.Append(value[i]);
				else if (index < to.Length)
					builder.Append(to[index]);
			}
			return builder.ToString();
		}

		private static void SkipWhitespace(string text, ref int position)
		{
			while (position < text.Length && char.IsWhiteSpace(text[position]))
				position++;
		}

		private static bool TryConsumeChar(string text, ref int position, char expected)
		{
			if (position < text.Length && text[position] == expected)
			{
				position++;
				return true;
			}
			return false;
		}

		private static bool TryConsumeWord(string text, ref int position, string word)
		{
			if (position + word.Length > text.Length)
				return false;
			if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
				return false;
			var next = position + word.Length;
			if (next < text.Length && IsNameChar(text[next]))
				return false;
			position = next;
			return true;
		}

		private static bool TryReadQuoted(string text, ref int position, out string value)
		{
			value = null;
			if (!TryConsumeChar(text, ref position, '"'))
				return false;
			var start = position;
			while (position < text.Length && text[position] != '"')
				position++;
			if (position >= text.Length)
				return false;
			value = text.Substring(start, position - start);
			position++;
			return true;
		}

		private static string ReadName(string text, ref int position)
		{
			if (position >= text.Length || !IsNameStart(text[position]))
				return null;
			var start = position++;
			while (position < text.Length && IsNameChar(text[position]))
				position++;
			return text.Substring(start, position - start);
		}
	}
}