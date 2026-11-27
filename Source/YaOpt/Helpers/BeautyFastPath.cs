using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Replaces the work <see cref="BeautyUtility.AverageBeautyPerceptible"/> does with a single pass
	/// over precomputed def-level tables.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Vanilla samples 241 cells, asks the region grid for a room twice per cell, and evaluates a full
	/// <c>StatWorker</c> pipeline for every beauty-relevant thing. Neither <c>Beauty</c> nor
	/// <c>BeautyOutdoors</c> is <c>immutable</c> or <c>cacheable</c>, and <c>GetStatValue</c> passes
	/// <c>cacheStaleAfterTicks = -1</c>, so all three vanilla stat caches are bypassed and every thing
	/// pays for <c>StatRequest</c> construction, a linear <c>statBases</c> scan, four stuff list
	/// scans, two comp loops, the two <c>StatPart</c>s, the scenario factor walk and the rounding.
	/// </para>
	/// <para>
	/// Everything that pipeline does is a pure function of <c>(stat, def, stuff, quality)</c> except
	/// the comp offsets/factors, <c>IBeautyContainer.BeautyOffset</c> and the scenario factor. The
	/// first group is folded into flat arrays indexed by <see cref="Def.index"/>; the comp loops are
	/// skipped for the defs whose comps all inherit the identity implementation, which is decided once
	/// per def because a thing's comps are exactly its def's <c>comps</c> entries. The scenario factor
	/// is hoisted to one lookup per sampled position instead of one per thing. The arithmetic below
	/// reproduces vanilla term by term, including its <c>v &gt; 0</c> / <c>!(v &lt;= 0)</c> asymmetry,
	/// the offset-before-factor comp order and the two-stage rounding, so results are bit-identical.
	/// </para>
	/// <para>
	/// Tables are rebuilt lazily after <see cref="Current.Game"/> exists and again whenever the def
	/// counts change, and dropped on <see cref="UpdateCallbackHelper.ClearCache"/>. A mod that
	/// rewrites beauty stat bases or the beauty <c>StatPart</c> list while a game is running is not
	/// picked up until the next load.
	/// </para>
	/// </remarks>
	/// <seealso cref="Patches.RimWorld_BeautyUtility_AverageBeautyPerceptible"/>
	/// <seealso cref="YaOptSettings.OptBeautyFastPath"/>
	internal static class BeautyFastPath
	{
		/// <summary>Slots per stat in the quality tables: one per <see cref="QualityCategory"/>.</summary>
		private const int QualityStride = 7;

		/// <summary>Offset of the <c>BeautyOutdoors</c> block inside the quality tables.</summary>
		private const int BeautyOutdoorsSlot = QualityStride;

		/// <summary>
		/// <c>StatPart_Quality</c> multipliers, laid out as <c>[Beauty 0..6][BeautyOutdoors 0..6]</c>.
		/// </summary>
		private static readonly float[] QualityMultiplier = new float[QualityStride * 2];

		/// <summary>
		/// <c>StatPart_Quality</c> gain caps, same layout as <see cref="QualityMultiplier"/>.
		/// </summary>
		private static readonly float[] QualityMaxGain = new float[QualityStride * 2];

		/// <summary>
		/// Base value of <c>Beauty</c> per <see cref="ThingDef"/>, before stuff, comps and parts.
		/// </summary>
		private static float[] _beautyBase;

		/// <summary>Base value of <c>BeautyOutdoors</c> per <see cref="ThingDef"/>.</summary>
		private static float[] _beautyOutdoorsBase;

		/// <summary>
		/// <c>ThingDef.StatBaseDefined(BeautyOutdoors)</c> per def. Vanilla calls this linear scan once
		/// per outdoor thing inside <c>Thing.GetBeauty</c>.
		/// </summary>
		private static bool[] _beautyOutdoorsDefined;

		/// <summary><see cref="BeautyUtility.BeautyRelevant"/> per <see cref="ThingCategory"/>.</summary>
		private static bool[] _beautyRelevant;

		/// <summary>
		/// Whether a def can occupy more than one cell. Single-cell things cannot be met twice in the
		/// radial walk, so they skip the cross-cell de-duplication set entirely.
		/// </summary>
		private static bool[] _multiCell;

		/// <summary>
		/// Whether the def owns a comp that can change a stat. Vanilla walks every comp of every
		/// <see cref="ThingWithComps"/> twice inside <c>GetValueUnfinalized</c>, and the default
		/// implementation returns the identity, so a def whose comps all inherit the default can skip
		/// both loops outright.
		/// </summary>
		private static bool[] _needsCompScan;

		private static float[] _stuffFactor;

		private static float[] _stuffFactorOutdoors;

		private static float[] _stuffOffset;

		private static float[] _stuffOffsetOutdoors;

		/// <summary>
		/// Quality-variant stuff factors, <c>[stuffIndex * 7 + quality]</c>. Null unless at least one
		/// stuff def declares a quality factor for the stat, which no vanilla stuff does.
		/// </summary>
		private static float[] _stuffFactorQuality;

		private static float[] _stuffFactorQualityOutdoors;

		/// <summary>Quality-variant stuff offsets. Null under the same condition as the factors.</summary>
		private static float[] _stuffOffsetQuality;

		private static float[] _stuffOffsetQualityOutdoors;

		private static float[] _terrainBeauty;

		private static float[] _terrainBeautyOutdoors;

		private static bool[] _terrainBeautyOutdoorsDefined;

		private static StatDef _beautyStat;

		private static StatDef _beautyOutdoorsStat;

		private static StatParams _beautyParams;

		private static StatParams _beautyOutdoorsParams;

		private static int _thingCount = -1;

		private static int _terrainCount = -1;

		/// <summary>
		/// Set when the loaded defs do not match what this rewrite assumes. The patch then defers to
		/// vanilla for the rest of the session instead of producing different numbers.
		/// </summary>
		private static bool _disabled;

		private static readonly HashSet<Thing> CountedThings = new HashSet<Thing>();

		private static readonly List<Room> VisibleRooms = new List<Room>(8);

		/// <summary>
		/// Comp type to "can change a stat". Method resolution is static, so the answer is cached for
		/// the process rather than per def, and cleared on load only because that is when the mod list
		/// may have changed underneath us.
		/// </summary>
		private static readonly Dictionary<Type, bool> CompStatContributors = new Dictionary<Type, bool>();

		static BeautyFastPath()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Invalidate);
		}

		/// <summary>
		/// Drops every table so the next call rebuilds from the current defs.
		/// </summary>
		internal static void Invalidate()
		{
			_beautyBase = null;
			_beautyOutdoorsBase = null;
			_beautyOutdoorsDefined = null;
			_beautyRelevant = null;
			_multiCell = null;
			_needsCompScan = null;
			_stuffFactor = null;
			_stuffFactorOutdoors = null;
			_stuffOffset = null;
			_stuffOffsetOutdoors = null;
			_stuffFactorQuality = null;
			_stuffFactorQualityOutdoors = null;
			_stuffOffsetQuality = null;
			_stuffOffsetQualityOutdoors = null;
			_terrainBeauty = null;
			_terrainBeautyOutdoors = null;
			_terrainBeautyOutdoorsDefined = null;
			_thingCount = -1;
			_terrainCount = -1;
			_disabled = false;
			CompStatContributors.Clear();
		}

		/// <summary>
		/// Computes <c>AverageBeautyPerceptible</c>.
		/// </summary>
		/// <returns>
		/// False when the fast path cannot run and the caller must fall through to vanilla. The result
		/// is written to <paramref name="result"/> only when this returns true.
		/// </returns>
		internal static bool TryCompute(IntVec3 root, Map map, out float result)
		{
			result = 0f;
			if (!EnsureReady())
				return false;

			if (!root.IsValid || !root.InBounds(map))
				return true;

			var relevantCells = BeautyUtility.beautyRelevantCells;
			relevantCells.Clear();
			CountedThings.Clear();
			VisibleRooms.Clear();

			var rootRoom = root.GetRoom(map);
			if (rootRoom == null)
				return true;

			AddVisibleRoom(rootRoom);
			if (rootRoom.IsDoorway)
			{
				// Vanilla treats a doorway as the union of the rooms its first region touches.
				foreach (var neighbor in rootRoom.FirstRegion.Neighbors)
					AddVisibleRoom(neighbor.Room);
			}

			var scenarioBeauty = ScenarioFactor(_beautyStat);
			var scenarioBeautyOutdoors = ScenarioFactor(_beautyOutdoorsStat);

			var pattern = GenRadial.RadialPattern;
			var sampleCells = BeautyUtility.SampleNumCells_Beauty;

			var sum = 0f;
			for (var i = 0; i < sampleCells; i++)
			{
				var cell = root + pattern[i];
				if (!cell.InBounds(map) || cell.Fogged(map))
					continue;

				// The room is resolved once here and reused as the cell's outdoors flag below, which
				// is the second GetRoom vanilla does in CellBeauty.
				var room = cell.GetRoom(map);
				if (!IsVisibleRoom(room) && !TouchesVisibleRoom(cell, map))
					continue;

				relevantCells.Add(cell);
				var outdoors = room == null || room.PsychologicallyOutdoors;
				sum += CellBeauty(cell, map, outdoors, scenarioBeauty, scenarioBeautyOutdoors);
			}

			CountedThings.Clear();
			VisibleRooms.Clear();

			var count = relevantCells.Count;
			if (count == 0)
				return true;

			result = sum / count;
#if DEBUG
			VerifyVanilla(root, map, result);
#endif
			return true;
		}

		/// <summary>
		/// Mirrors <c>BeautyUtility.CellBeauty</c>.
		/// </summary>
		private static float CellBeauty(IntVec3 cell, Map map, bool outdoors, float scenarioBeauty,
			float scenarioBeautyOutdoors)
		{
			var sum = 0f;
			var fullSum = 0f;
			var hasFull = false;
			var roofed = map.roofGrid.Roofed(cell);
			var things = map.thingGrid.ThingsListAt(cell);

			for (var i = 0; i < things.Count; i++)
			{
				var thing = things[i];
				var def = thing.def;
				var defIndex = def.index;
				if (!_beautyRelevant[defIndex])
					continue;

				if (_multiCell[defIndex] && !CountedThings.Add(thing))
					continue;

				// Vanilla calls GetSlotGroup before testing EverHaulable, so every wall, plant and
				// piece of filth pays for a grid lookup whose result is then discarded. The call has
				// no side effects, so testing the def first is free.
				if (def.EverHaulable)
				{
					var slotGroup = thing.GetSlotGroup();
					if (slotGroup != null && slotGroup.parent != thing && slotGroup.parent.IgnoreStoredThingsBeauty)
						continue;
				}

				var value = ThingBeauty(thing, def, defIndex, outdoors, scenarioBeauty, scenarioBeautyOutdoors);

				if (def.filth != null && roofed)
					value *= 0.3f;

				if (def.Fillage == FillCategory.Full)
				{
					hasFull = true;
					fullSum += value;
				}
				else
				{
					sum += value;
				}
			}

			if (hasFull)
				return fullSum;

			var terrain = map.terrainGrid.TerrainAt(cell);
			var terrainIndex = terrain.index;
			if (ModsConfig.BiotechActive && !terrain.BuildableByPlayer && cell.IsPolluted(map))
				sum -= 1f;

			if (outdoors && _terrainBeautyOutdoorsDefined[terrainIndex])
				return sum + Finalize(_terrainBeautyOutdoors[terrainIndex] * scenarioBeautyOutdoors, true);

			return sum + Finalize(_terrainBeauty[terrainIndex] * scenarioBeauty, false);
		}

		/// <summary>
		/// Mirrors <c>Thing.GetBeauty(outside)</c> together with the stat pipeline it invokes.
		/// </summary>
		private static float ThingBeauty(Thing thing, ThingDef def, int defIndex, bool outdoors,
			float scenarioBeauty, float scenarioBeautyOutdoors)
		{
			var useOutdoors = outdoors && _beautyOutdoorsDefined[defIndex];
			var value = useOutdoors ? _beautyOutdoorsBase[defIndex] : _beautyBase[defIndex];

			// StatRequest.For reads this once and vanilla reads it again inside both stuff branches.
			var hasQuality = thing.TryGetQuality(out var quality);
			var qualityIndex = (int)quality;

			var stuff = thing.Stuff;
			if (stuff != null)
			{
				var stuffIndex = stuff.index;
				var qualitySlot = stuffIndex * QualityStride + qualityIndex;
				var factorQuality = useOutdoors ? _stuffFactorQualityOutdoors : _stuffFactorQuality;
				var offsetQuality = useOutdoors ? _stuffOffsetQualityOutdoors : _stuffOffsetQuality;

				if (value > 0f)
				{
					value *= (useOutdoors ? _stuffFactorOutdoors : _stuffFactor)[stuffIndex];
					if (hasQuality && factorQuality != null)
						value *= factorQuality[qualitySlot];
				}

				value += (useOutdoors ? _stuffOffsetOutdoors : _stuffOffset)[stuffIndex];
				if (hasQuality && offsetQuality != null)
					value += offsetQuality[qualitySlot];
			}

			var stat = useOutdoors ? _beautyOutdoorsStat : _beautyStat;
			if (_needsCompScan[defIndex] && thing is ThingWithComps withComps)
			{
				// Vanilla applies every comp offset first, then every comp factor.
				var comps = withComps.AllComps;
				for (var i = 0; i < comps.Count; i++)
					value += comps[i].GetStatOffset(stat);
				for (var i = 0; i < comps.Count; i++)
					value *= comps[i].GetStatFactor(stat);
			}

			// StatPart_Quality tests `!(val <= 0f)`, not `val > 0f`; the two differ for NaN.
			if (!(value <= 0f))
			{
				var slot = (useOutdoors ? BeautyOutdoorsSlot : 0) + qualityIndex;
				value += Mathf.Min(value * QualityMultiplier[slot] - value, QualityMaxGain[slot]);
			}

			if (thing is IBeautyContainer container)
				value += container.BeautyOffset;

			value *= useOutdoors ? scenarioBeautyOutdoors : scenarioBeauty;
			return Finalize(value, useOutdoors);
		}

		/// <summary>
		/// Mirrors the tail of <c>StatWorker.FinalizeValue</c>: scenario factor already applied, then
		/// the five-point snap, the integer snap and the clamp.
		/// </summary>
		private static float Finalize(float value, bool outdoors)
		{
			var parameters = outdoors ? _beautyOutdoorsParams : _beautyParams;
			if (Mathf.Abs(value) > parameters.RoundToFiveOver)
				value = Mathf.Round(value / 5f) * 5f;
			if (parameters.RoundValue)
				value = Mathf.RoundToInt(value);
			return Mathf.Clamp(value, parameters.Min, parameters.Max);
		}

		private static float ScenarioFactor(StatDef stat)
		{
			var scenario = Find.Scenario;
			return scenario == null ? 1f : scenario.GetStatFactor(stat);
		}

		private static void AddVisibleRoom(Room room)
		{
			for (var i = 0; i < VisibleRooms.Count; i++)
			{
				if (ReferenceEquals(VisibleRooms[i], room))
					return;
			}

			VisibleRooms.Add(room);
		}

		private static bool IsVisibleRoom(Room room)
		{
			for (var i = 0; i < VisibleRooms.Count; i++)
			{
				if (ReferenceEquals(VisibleRooms[i], room))
					return true;
			}

			return false;
		}

		private static bool TouchesVisibleRoom(IntVec3 cell, Map map)
		{
			for (var i = 0; i < 8; i++)
			{
				if (IsVisibleRoom((cell + GenAdj.AdjacentCells[i]).GetRoom(map)))
					return true;
			}

			return false;
		}

		private static bool EnsureReady()
		{
			if (_disabled)
				return false;

			var thingCount = DefDatabase<ThingDef>.DefCount;
			var terrainCount = DefDatabase<TerrainDef>.DefCount;
			if (thingCount == 0 || terrainCount == 0)
				return false;

			// Before a game exists the def list is still being edited, so caching here would freeze
			// intermediate values.
			if (Current.Game == null)
				return false;

			if (_beautyBase != null && _thingCount == thingCount && _terrainCount == terrainCount)
				return true;

			if (!Build(thingCount, terrainCount))
				return false;

			_thingCount = thingCount;
			_terrainCount = terrainCount;
			return true;
		}

		private static bool Build(int thingCount, int terrainCount)
		{
			_beautyStat = StatDefOf.Beauty;
			_beautyOutdoorsStat = StatDefOf.BeautyOutdoors;
			if (_beautyStat == null || _beautyOutdoorsStat == null)
				return false;

			if (!LoadStatParameters(_beautyStat, 0, out _beautyParams)
				|| !LoadStatParameters(_beautyOutdoorsStat, BeautyOutdoorsSlot, out _beautyOutdoorsParams))
			{
				_disabled = true;
				return false;
			}

			var beautyBase = new float[thingCount];
			var beautyOutdoorsBase = new float[thingCount];
			var beautyOutdoorsDefined = new bool[thingCount];
			var relevant = new bool[thingCount];
			var multiCell = new bool[thingCount];
			var needsCompScan = new bool[thingCount];
			var stuffFactor = new float[thingCount];
			var stuffFactorOutdoors = new float[thingCount];
			var stuffOffset = new float[thingCount];
			var stuffOffsetOutdoors = new float[thingCount];

			// A foreign patch on the base comp hooks would make the per-def gate wrong for every def,
			// because the contribution would no longer come from an override.
			var compsPatchedEverywhere = CompStatHooksPatched();

			var thingDefs = DefDatabase<ThingDef>.AllDefsListForReading;
			for (var i = 0; i < thingDefs.Count; i++)
			{
				var def = thingDefs[i];
				var index = def.index;
				if (index >= thingCount)
					continue;

				beautyBase[index] = BaseValue(def.statBases, _beautyStat);
				beautyOutdoorsBase[index] = BaseValue(def.statBases, _beautyOutdoorsStat);
				beautyOutdoorsDefined[index] = def.StatBaseDefined(_beautyOutdoorsStat);
				relevant[index] = BeautyUtility.BeautyRelevant(def.category);
				multiCell[index] = def.size.x > 1 || def.size.z > 1;
				needsCompScan[index] = compsPatchedEverywhere || DefHasStatComp(def);

				var stuff = def.stuffProps;
				stuffFactor[index] = 1f;
				stuffFactorOutdoors[index] = 1f;
				if (stuff == null)
					continue;

				stuffFactor[index] = StatFactor(stuff.statFactors, _beautyStat);
				stuffFactorOutdoors[index] = StatFactor(stuff.statFactors, _beautyOutdoorsStat);
				stuffOffset[index] = StatOffset(stuff.statOffsets, _beautyStat);
				stuffOffsetOutdoors[index] = StatOffset(stuff.statOffsets, _beautyOutdoorsStat);
			}

			var stuffFactorQuality = BuildStuffQuality(thingDefs, thingCount, true, _beautyStat);
			var stuffFactorQualityOutdoors = BuildStuffQuality(thingDefs, thingCount, true, _beautyOutdoorsStat);
			var stuffOffsetQuality = BuildStuffQuality(thingDefs, thingCount, false, _beautyStat);
			var stuffOffsetQualityOutdoors = BuildStuffQuality(thingDefs, thingCount, false, _beautyOutdoorsStat);

			var terrainBeauty = new float[terrainCount];
			var terrainBeautyOutdoors = new float[terrainCount];
			var terrainBeautyOutdoorsDefined = new bool[terrainCount];
			var terrainDefs = DefDatabase<TerrainDef>.AllDefsListForReading;
			for (var i = 0; i < terrainDefs.Count; i++)
			{
				var def = terrainDefs[i];
				var index = def.index;
				if (index >= terrainCount)
					continue;

				// A terrain request has no thing, so no comps and no contents; only the quality part
				// runs, always at QualityCategory.Normal.
				terrainBeauty[index] = ApplyQualityPart(BaseValue(def.statBases, _beautyStat), 0);
				terrainBeautyOutdoors[index] =
					ApplyQualityPart(BaseValue(def.statBases, _beautyOutdoorsStat), BeautyOutdoorsSlot);
				terrainBeautyOutdoorsDefined[index] = def.StatBaseDefined(_beautyOutdoorsStat);
			}

			// Publish the leaf arrays first: a reader that sees a non-null _beautyBase also sees every
			// table the hot path indexes alongside it.
			_stuffFactorQuality = stuffFactorQuality;
			_stuffFactorQualityOutdoors = stuffFactorQualityOutdoors;
			_stuffOffsetQuality = stuffOffsetQuality;
			_stuffOffsetQualityOutdoors = stuffOffsetQualityOutdoors;
			_stuffFactor = stuffFactor;
			_stuffFactorOutdoors = stuffFactorOutdoors;
			_stuffOffset = stuffOffset;
			_stuffOffsetOutdoors = stuffOffsetOutdoors;
			_multiCell = multiCell;
			_needsCompScan = needsCompScan;
			_beautyRelevant = relevant;
			_beautyOutdoorsDefined = beautyOutdoorsDefined;
			_beautyOutdoorsBase = beautyOutdoorsBase;
			_terrainBeauty = terrainBeauty;
			_terrainBeautyOutdoors = terrainBeautyOutdoors;
			_terrainBeautyOutdoorsDefined = terrainBeautyOutdoorsDefined;
			_beautyBase = beautyBase;
			return true;
		}

		/// <summary>
		/// Reads the two <c>StatPart</c>s this rewrite reproduces and refuses to run if the stat no
		/// longer looks like what the arithmetic below assumes.
		/// </summary>
		private static bool LoadStatParameters(StatDef stat, int slot, out StatParams parameters)
		{
			parameters = default;

			if (stat.parts == null || stat.parts.Count != 2
				|| !(stat.parts[0] is StatPart_Quality quality)
				|| !(stat.parts[1] is StatPart_ContentsBeauty))
			{
				YaOptMod.Error($"{stat.defName} no longer has exactly [StatPart_Quality, " +
					"StatPart_ContentsBeauty]; the beauty fast path is disabled.");
				return false;
			}

			// applyFactorsIfNegative decides whether the stuff factor branch is taken for negative
			// values, so it cannot be deferred to the runtime tables.
			if (stat.applyFactorsIfNegative
				|| stat.postProcessCurve != null
				|| (stat.postProcessStatFactors != null && stat.postProcessStatFactors.Count > 0)
				|| (stat.statFactors != null && stat.statFactors.Count > 0))
			{
				YaOptMod.Error($"{stat.defName} enables post-processing the beauty fast path does not " +
					"reproduce; the beauty fast path is disabled.");
				return false;
			}

			QualityMultiplier[slot + 0] = quality.factorAwful;
			QualityMultiplier[slot + 1] = quality.factorPoor;
			QualityMultiplier[slot + 2] = quality.factorNormal;
			QualityMultiplier[slot + 3] = quality.factorGood;
			QualityMultiplier[slot + 4] = quality.factorExcellent;
			QualityMultiplier[slot + 5] = quality.factorMasterwork;
			QualityMultiplier[slot + 6] = quality.factorLegendary;

			QualityMaxGain[slot + 0] = quality.maxGainAwful;
			QualityMaxGain[slot + 1] = quality.maxGainPoor;
			QualityMaxGain[slot + 2] = quality.maxGainNormal;
			QualityMaxGain[slot + 3] = quality.maxGainGood;
			QualityMaxGain[slot + 4] = quality.maxGainExcellent;
			QualityMaxGain[slot + 5] = quality.maxGainMasterwork;
			QualityMaxGain[slot + 6] = quality.maxGainLegendary;

			parameters.Min = stat.minValue;
			parameters.Max = stat.maxValue;
			parameters.RoundToFiveOver = stat.roundToFiveOver;
			parameters.RoundValue = stat.roundValue;
			return true;
		}

		/// <summary>
		/// Applies <c>StatPart_Quality.TransformValue</c> at <see cref="QualityCategory.Normal"/>,
		/// which is what every abstract request uses.
		/// </summary>
		private static float ApplyQualityPart(float value, int slot)
		{
			if (value <= 0f)
				return value;

			var index = slot + (int)QualityCategory.Normal;
			return value + Mathf.Min(value * QualityMultiplier[index] - value, QualityMaxGain[index]);
		}

		/// <summary>
		/// Reproduces <c>StatWorker.GetBaseValueFor</c> for a def-level request.
		/// </summary>
		private static float BaseValue(List<StatModifier> statBases, StatDef stat)
		{
			if (statBases != null)
			{
				for (var i = 0; i < statBases.Count; i++)
				{
					if (statBases[i].stat == stat)
						return statBases[i].value;
				}
			}

			return stat.defaultBaseValue;
		}

		private static float StatFactor(List<StatModifier> modifiers, StatDef stat)
		{
			return modifiers == null ? 1f : modifiers.GetStatValueFromList(stat, 1f);
		}

		private static float StatOffset(List<StatModifier> modifiers, StatDef stat)
		{
			return modifiers == null ? 0f : modifiers.GetStatValueFromList(stat, 0f);
		}

		/// <summary>
		/// Whether any of the def's comps can change a stat. <c>ThingWithComps.InitializeComps</c>
		/// instantiates exactly one comp per <c>def.comps</c> entry and nothing else adds comps, so the
		/// comp list of a thing is a pure function of its def and this answer is exact.
		/// </summary>
		private static bool DefHasStatComp(ThingDef def)
		{
			var comps = def.comps;
			if (comps == null || comps.Count == 0)
				return false;

			for (var i = 0; i < comps.Count; i++)
			{
				if (CompCanChangeStat(comps[i]?.compClass))
					return true;
			}

			return false;
		}

		private static bool CompCanChangeStat(Type compClass)
		{
			// A def we cannot inspect keeps the vanilla scan.
			if (compClass == null)
				return true;

			if (CompStatContributors.TryGetValue(compClass, out var known))
				return known;

			var contributes = OverridesCompStatHook(compClass, nameof(ThingComp.GetStatOffset))
				|| OverridesCompStatHook(compClass, nameof(ThingComp.GetStatFactor));
			CompStatContributors[compClass] = contributes;
			return contributes;
		}

		private static bool OverridesCompStatHook(Type compClass, string methodName)
		{
			try
			{
				var method = compClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(StatDef) }, null);
				return method != null && method.DeclaringType != typeof(ThingComp);
			}
			catch (AmbiguousMatchException)
			{
				return true;
			}
		}

		/// <summary>
		/// A foreign patch on the base comp hooks contributes stats without an override, which the
		/// per-def gate cannot see. Treated as "every def needs the scan".
		/// </summary>
		private static bool CompStatHooksPatched()
		{
			return PatchConflictGuard.HasForeignPatch(
					AccessTools.Method(typeof(ThingComp), nameof(ThingComp.GetStatOffset)))
				|| PatchConflictGuard.HasForeignPatch(
					AccessTools.Method(typeof(ThingComp), nameof(ThingComp.GetStatFactor)));
		}

		/// <summary>
		/// Expands a quality-variant stuff list into <c>[stuffIndex * 7 + quality]</c>, or returns
		/// null when no stuff def declares one for the stat.
		/// </summary>
		private static float[] BuildStuffQuality(List<ThingDef> thingDefs, int thingCount, bool factors,
			StatDef stat)
		{
			var any = false;
			for (var i = 0; i < thingDefs.Count; i++)
			{
				var stuff = thingDefs[i].stuffProps;
				if (stuff == null)
					continue;

				var list = factors ? stuff.statFactorsQuality : stuff.statOffsetsQuality;
				if (list != null && list.Count > 0)
				{
					any = true;
					break;
				}
			}

			if (!any)
				return null;

			var table = new float[thingCount * QualityStride];
			var fallback = factors ? 1f : 0f;
			for (var i = 0; i < table.Length; i++)
				table[i] = fallback;

			for (var i = 0; i < thingDefs.Count; i++)
			{
				var def = thingDefs[i];
				var index = def.index;
				var stuff = def.stuffProps;
				if (stuff == null || index >= thingCount)
					continue;

				var list = factors ? stuff.statFactorsQuality : stuff.statOffsetsQuality;
				if (list == null || list.Count == 0)
					continue;

				var start = index * QualityStride;
				for (var quality = 0; quality < QualityStride; quality++)
					table[start + quality] = list.GetStatValueFromList(stat, (QualityCategory)quality, fallback);
			}

			return table;
		}

#if DEBUG
		private static int _mismatches;

		/// <summary>
		/// Debug-only: recomputes the same figure through the untouched
		/// <see cref="BeautyUtility.FillBeautyRelevantCells"/> and <see cref="BeautyUtility.CellBeauty"/>
		/// and reports any difference, so a broken equivalence shows up instead of silently changing
		/// need values.
		/// </summary>
		private static void VerifyVanilla(IntVec3 root, Map map, float fastResult)
		{
			try
			{
				BeautyUtility.FillBeautyRelevantCells(root, map);
				var cells = BeautyUtility.beautyRelevantCells;
				var counted = new HashSet<Thing>();
				var sum = 0f;
				for (var i = 0; i < cells.Count; i++)
					sum += BeautyUtility.CellBeauty(cells[i], map, counted);

				var vanillaResult = cells.Count == 0 ? 0f : sum / cells.Count;
				if (vanillaResult.Equals(fastResult))
					return;

				if (_mismatches++ < 20)
				{
					YaOptMod.Error($"Beauty fast path mismatch at {root} on {map}: " +
						$"fast={fastResult} vanilla={vanillaResult} cells={cells.Count}");
				}
			}
			catch (System.Exception ex)
			{
				YaOptMod.Error($"Beauty fast path verification threw: {ex}");
			}
		}
#endif

		/// <summary>
		/// The parts of a <see cref="StatDef"/> that <see cref="Finalize"/> needs.
		/// </summary>
		private struct StatParams
		{
			internal float Min;

			internal float Max;

			internal float RoundToFiveOver;

			internal bool RoundValue;
		}
	}
}
