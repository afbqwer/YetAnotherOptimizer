using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Wraps the value callbacks of <c>GenerateDefsWithPossibleTotalValue</c> with a per-call memo.
	/// </summary>
	/// <remarks>
	/// Vanilla asks for the same stat up to three times per candidate. The wrapper is only applied to
	/// callbacks declared in the vanilla assembly, because a modded callback could be stateful or random
	/// and must keep running every time.
	/// </remarks>
	internal static class GenerationStatMemo
	{
		private static readonly Assembly VanillaAssembly = typeof(ThingStuffPairWithQuality).Assembly;

		internal static Func<ThingStuffPairWithQuality, float> Wrap(Func<ThingStuffPairWithQuality, float> source)
		{
			if (source == null)
				return null;

			var declaringType = source.Method.DeclaringType;
			if (declaringType == null || declaringType.Assembly != VanillaAssembly)
				return source;

			var cache = new Dictionary<ThingStuffPairWithQuality, float>();
			return pair =>
			{
				if (cache.TryGetValue(pair, out var value))
					return value;

				value = source(pair);
				cache[pair] = value;
				return value;
			};
		}
	}
}
