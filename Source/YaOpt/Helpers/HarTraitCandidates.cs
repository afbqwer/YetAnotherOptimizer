using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Materialises Humanoid Alien Races' permitted trait list once per
	/// <see cref="PawnGenerator.GenerateTraitsFor"/> call.
	/// </summary>
	/// <remarks>
	/// HAR wraps <see cref="DefDatabase{T}.AllDefsListForReading"/> in a <c>Where</c> that runs
	/// <c>RaceRestrictionSettings.CanGetTrait</c>. That method builds a list of the pawn's backstory
	/// restrictions and runs HAR chance rolls, so the weighted sampler's second pass repeats all of it.
	/// The permitted defs only depend on the pawn during one trait generation, so they are validated once
	/// and reused. HAR's chance rolls therefore run once per candidate instead of once per sampler pass,
	/// which is explicitly outside the compatibility contract.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnTraitsHarFastPath"/>
	/// <seealso cref="Patches.Compatibility.HumanoidAlienRaces.AlienRace_GenerateTraitsValidator"/>
	/// <seealso cref="Patches.Verse_PawnGenerator_GenerateTraitsFor"/>
	internal static class HarTraitCandidates
	{
		private static Func<TraitDef, Pawn, int, bool> _canGetTrait;

		private static MethodInfo _validator;

		internal static MethodInfo Validator => _validator;

		internal static bool Ready => _canGetTrait != null && _validator != null;

		[ThreadStatic]
		private static bool _active;

		[ThreadStatic]
		private static Pawn _pawn;

		[ThreadStatic]
		private static bool _built;

		[ThreadStatic]
		private static List<TraitDef> _candidates;

		[ThreadStatic]
		private static List<TraitDef> _source;

		internal static bool Configure()
		{
			_validator = null;
			_canGetTrait = null;
			_active = false;
			_pawn = null;
			_built = false;
			_source = null;

			try
			{
				var patchesType = AccessTools.TypeByName("AlienRace.HarmonyPatches");
				var restrictionsType = AccessTools.TypeByName("AlienRace.RaceRestrictionSettings");
				if (patchesType == null || restrictionsType == null)
					return false;

				_validator = AccessTools.Method(patchesType, "GenerateTraitsValidator",
					new[] { typeof(List<TraitDef>), typeof(Pawn) });
				var canGetTrait = AccessTools.Method(restrictionsType, "CanGetTrait",
					new[] { typeof(TraitDef), typeof(Pawn), typeof(int) });
				if (_validator == null || canGetTrait == null)
					return false;

				_canGetTrait = AccessTools.MethodDelegate<Func<TraitDef, Pawn, int, bool>>(canGetTrait);
				return _canGetTrait != null;
			}
			catch (Exception ex)
			{
				_validator = null;
				_canGetTrait = null;
				YaOptMod.Debug("HAR trait candidate cache unavailable: " + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Opens a candidate session for <paramref name="pawn"/> unless one is already open.
		/// </summary>
		/// <returns>True when this call opened the session and must close it again.</returns>
		internal static bool Begin(Pawn pawn)
		{
			if (!Ready || _active || pawn == null)
				return false;

			_active = true;
			_pawn = pawn;
			_built = false;
			_source = null;
			return true;
		}

		internal static void End()
		{
			_active = false;
			_pawn = null;
			_built = false;
			_source = null;
			_candidates?.Clear();
		}

		/// <summary>
		/// Returns the materialised permitted trait defs, or null when the caller must run HAR's lazy
		/// validator.
		/// </summary>
		internal static List<TraitDef> Get(Pawn pawn, List<TraitDef> source)
		{
			if (!_active || !Ready || pawn == null || pawn != _pawn || source == null)
				return null;

			if (!_built || source != _source)
			{
				if (_candidates == null)
					_candidates = new List<TraitDef>(source.Count);
				else
					_candidates.Clear();

				for (var i = 0; i < source.Count; i++)
				{
					var trait = source[i];
					if (_canGetTrait(trait, pawn, 0))
						_candidates.Add(trait);
				}

				_source = source;
				_built = true;
			}

			return _candidates;
		}
	}
}