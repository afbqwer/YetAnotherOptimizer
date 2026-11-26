using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Environment inputs for one apparel generation call.
	/// </summary>
	/// <remarks>
	/// With live environment enabled and a map available, temperature comes from the map's current
	/// outdoor reading rather than the world-tile seasonal average, pollution comes from the map's
	/// synced world tile plus active toxic fallout, and vacuum comes from the destination biome. The
	/// map is resolved once and every remaining read is a field lookup; without a map the exact
	/// vanilla methods are used.
	/// </remarks>
	internal readonly struct PawnGearEnvironment
	{
		private const float WarmthMargin = 4f;

		internal readonly NeededWarmth Warmth;

		internal readonly float MapTemperature;

		internal readonly float Pollution;

		internal readonly bool NeedVacuum;

		internal readonly float BaseComfyMin;

		internal readonly float BaseComfyMax;

		internal readonly bool HasBaseComfyRange;

		private PawnGearEnvironment(NeededWarmth warmth, float mapTemperature, float pollution,
			bool needVacuum, float baseComfyMin, float baseComfyMax, bool hasBaseComfyRange)
		{
			Warmth = warmth;
			MapTemperature = mapTemperature;
			Pollution = pollution;
			NeedVacuum = needVacuum;
			BaseComfyMin = baseComfyMin;
			BaseComfyMax = baseComfyMax;
			HasBaseComfyRange = hasBaseComfyRange;
		}

		internal static PawnGearEnvironment Resolve(Pawn pawn, PawnGenerationRequest request,
			bool useLiveEnvironment)
		{
			if (useLiveEnvironment)
			{
				var map = FindMap(pawn, request);
				if (map != null)
					return FromMap(pawn, request, map);
			}

			var warmth = PawnApparelGenerator.ApparelWarmthNeededNow(pawn, request,
				out var mapTemperature);
			var pollution = PawnApparelGenerator.ApparelToxicEnvironmentToAddress(pawn, request);
			var needVacuum = PawnApparelGenerator.NeedVacuumResistance(pawn, request);
			return new PawnGearEnvironment(warmth, mapTemperature, pollution, needVacuum, 0f, 0f,
				false);
		}

		private static PawnGearEnvironment FromMap(Pawn pawn, PawnGenerationRequest request,
			Map map)
		{
			var mapTemperature = map.mapTemperature.OutdoorTemp;
			var warmth = CalculateWarmth(pawn, request, mapTemperature, out var baseMin,
				out var baseMax, out var hasBaseRange);
			return new PawnGearEnvironment(warmth, mapTemperature, CalculatePollution(pawn, map),
				CalculateNeedVacuum(pawn, map), baseMin, baseMax, hasBaseRange);
		}

		private static NeededWarmth CalculateWarmth(Pawn pawn, PawnGenerationRequest request,
			float mapTemperature, out float baseMin, out float baseMax, out bool hasBaseRange)
		{
			baseMin = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin, applyPostProcess: true,
				cacheStaleAfterTicks: 1);
			var needsWarmth = mapTemperature < baseMin - WarmthMargin;

			if (pawn.kindDef.apparelIgnoreSeasons && !request.ForceAddFreeWarmLayerIfNeeded)
			{
				baseMax = 0f;
				hasBaseRange = false;
				return NeededWarmth.Any;
			}

			baseMax = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax, applyPostProcess: true,
				cacheStaleAfterTicks: 1);
			if (needsWarmth)
			{
				hasBaseRange = true;
				return NeededWarmth.Warm;
			}

			if (mapTemperature > baseMax + WarmthMargin)
			{
				hasBaseRange = true;
				return NeededWarmth.Cool;
			}

			hasBaseRange = false;
			return NeededWarmth.Any;
		}

		private static float CalculatePollution(Pawn pawn, Map map)
		{
			if (pawn.kindDef.apparelIgnorePollution || !ModsConfig.BiotechActive)
				return 0f;

			if (map.GameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout))
				return 1f;

			if (map.Tile.Valid)
				return Find.WorldGrid[map.Tile].pollution;

			// Pocket maps have no synced world tile and are not part of the pawn-generation hot path.
			return map.pollutionGrid.TotalPollutionPercent;
		}

		private static bool CalculateNeedVacuum(Pawn pawn, Map map)
		{
			return ModsConfig.OdysseyActive && pawn.ConcernedByVacuum
				&& map.Biome != null && map.Biome.inVacuum;
		}

		private static Map FindMap(Pawn pawn, PawnGenerationRequest request)
		{
			var pawnMap = pawn.MapHeld;
			var tile = request.Tile;
			if (!tile.Valid)
				return pawnMap;

			if (pawnMap != null && pawnMap.Tile == tile)
				return pawnMap;
			if (Find.Maps == null)
				return null;

			var maps = Find.Maps;
			for (var i = 0; i < maps.Count; i++)
			{
				if (maps[i].Tile == tile)
					return maps[i];
			}

			return null;
		}
	}
}