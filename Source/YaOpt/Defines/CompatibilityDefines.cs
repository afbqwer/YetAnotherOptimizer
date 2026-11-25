using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Defines
{
	public static class CompatibilityDefines
	{
		public static readonly HashSet<string> CachedBannedOptimizations = new HashSet<string>();

		public static readonly Dictionary<string, string> CachedBannedBy = new Dictionary<string, string>();

		public static readonly HashSet<Type> CachedIgnoredToggleTabCaching = new HashSet<Type>();

		public static void Load()
		{
			foreach (var def in DefDatabase<CompatibilityDef>.AllDefs)
			{
				var owner = def.modContentPack.Name;

				try
				{
					def.Cache();
				}
				catch (Exception e)
				{
					YaOptMod.Error($"Error when parsing CompatibilityDef {def.defName} from {owner}. " +
								   $"{e.ToStringSafe()}");
				}
			}
		}
	}
}
