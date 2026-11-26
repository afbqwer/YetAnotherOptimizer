using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces Humanoid Alien Races' apparel-properties lookup with a prebuilt def map.
	/// </summary>
	/// <remarks>
	/// HAR resolves <see cref="ApparelProperties"/> back to its <see cref="ThingDef"/> through
	/// <c>CachedData.GetApparelFromApparelProps</c>, which creates a closure over the argument before its
	/// own dictionary is consulted, so every call allocates even on a cache hit. Apparel generation calls
	/// it once per candidate pair. The map built here is the same first-match lookup over
	/// <see cref="DefDatabase{T}.AllDefsListForReading"/> and is rebuilt when the def count changes or a
	/// save loads, so shared <see cref="ApparelProperties"/> instances keep the original winner.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearHarFastPath"/>
	/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_CachedData_GetApparelFromApparelProps"/>
	internal static class HarGearCaches
	{
		private static Dictionary<ApparelProperties, ThingDef> _apparelByProps;

		private static int _defCount = -1;

		static HarGearCaches()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		internal static void Invalidate()
		{
			_apparelByProps = null;
			_defCount = -1;
		}

		internal static bool TryGetApparel(ApparelProperties props, out ThingDef def)
		{
			var count = DefDatabase<ThingDef>.DefCount;
			if (count != _defCount || _apparelByProps == null)
				BuildApparelMap(count);

			return _apparelByProps.TryGetValue(props, out def);
		}

		private static void BuildApparelMap(int defCount)
		{
			var defs = DefDatabase<ThingDef>.AllDefsListForReading;
			var map = new Dictionary<ApparelProperties, ThingDef>(defs.Count);
			for (var i = 0; i < defs.Count; i++)
			{
				var def = defs[i];
				var apparel = def.apparel;
				if (apparel != null && !map.ContainsKey(apparel))
					map[apparel] = def;
			}

			_apparelByProps = map;
			_defCount = defCount;
		}
	}
}