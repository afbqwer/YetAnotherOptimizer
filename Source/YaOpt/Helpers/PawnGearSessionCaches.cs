using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Reuses pawn-level values that stay constant for the duration of one apparel or weapon
	/// generation call.
	/// </summary>
	/// <remarks>
	/// Apparel generation retries the same candidate set up to 80 times and re-runs the same material
	/// restrictions and pawn temperature stats on every attempt. Weapon selection evaluates every
	/// candidate weight twice because the IList sampler makes two passes. All memoised values are pure
	/// functions of the pawn and the candidate pair within a single call, and every session is cleared
	/// when the vanilla call returns, so the random stream and the produced gear are unchanged.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptPawnGearSessionCache"/>
	/// <seealso cref="Patches.RimWorld_PawnWeaponGenerator_GetCommonality"/>
	/// <seealso cref="Patches.RimWorld_PawnApparelGenerator_CanUseStuff"/>
	/// <seealso cref="Patches.Verse_GenTemperature_SafeTemperatureRange"/>
	internal static class PawnGearSessionCaches
	{
		[ThreadStatic]
		private static bool _weaponSessionActive;

		[ThreadStatic]
		private static Pawn _weaponPawn;

		[ThreadStatic]
		private static Dictionary<ThingStuffPair, float> _weaponCommonality;

		[ThreadStatic]
		private static bool _apparelSessionActive;

		[ThreadStatic]
		private static Pawn _apparelPawn;

		[ThreadStatic]
		private static Dictionary<ThingStuffPair, bool> _apparelStuffUsable;

		[ThreadStatic]
		private static bool _apparelBaseTemperatureValid;

		[ThreadStatic]
		private static float _apparelComfyTemperatureMin;

		[ThreadStatic]
		private static float _apparelComfyTemperatureMax;

		[ThreadStatic]
		private static HashSet<BodyPartGroupDef> _apparelWearableGroups;

		[ThreadStatic]
		private static bool _apparelWearableGroupsValid;

		/// <summary>
		/// Opens a weapon session for <paramref name="pawn"/> unless one is already open.
		/// </summary>
		/// <returns>True when this call opened the session and must close it again.</returns>
		internal static bool BeginWeaponSession(Pawn pawn)
		{
			if (_weaponSessionActive)
				return false;

			if (_weaponCommonality == null)
				_weaponCommonality = new Dictionary<ThingStuffPair, float>();
			else
				_weaponCommonality.Clear();

			_weaponPawn = pawn;
			_weaponSessionActive = true;
			return true;
		}

		internal static void EndWeaponSession()
		{
			_weaponSessionActive = false;
			_weaponPawn = null;
			_weaponCommonality?.Clear();
		}

		internal static bool TryGetWeaponCommonality(Pawn pawn, ThingStuffPair pair, out float value)
		{
			value = 0f;
			if (!_weaponSessionActive || pawn != _weaponPawn)
				return false;

			return _weaponCommonality.TryGetValue(pair, out value);
		}

		internal static void StoreWeaponCommonality(Pawn pawn, ThingStuffPair pair, float value)
		{
			if (!_weaponSessionActive || pawn != _weaponPawn)
				return;

			_weaponCommonality[pair] = value;
		}

		/// <summary>
		/// Opens an apparel session for <paramref name="pawn"/> unless one is already open.
		/// </summary>
		/// <returns>True when this call opened the session and must close it again.</returns>
		internal static bool BeginApparelSession(Pawn pawn)
		{
			if (_apparelSessionActive)
				return false;

			if (_apparelStuffUsable == null)
				_apparelStuffUsable = new Dictionary<ThingStuffPair, bool>();
			else
				_apparelStuffUsable.Clear();

			_apparelPawn = pawn;
			_apparelBaseTemperatureValid = false;
			_apparelWearableGroupsValid = false;
			_apparelSessionActive = true;
			return true;
		}

		internal static void EndApparelSession()
		{
			_apparelSessionActive = false;
			_apparelPawn = null;
			_apparelBaseTemperatureValid = false;
			_apparelWearableGroupsValid = false;
			_apparelStuffUsable?.Clear();
			_apparelWearableGroups?.Clear();
		}

		/// <summary>
		/// Returns whether <see cref="Patches.RimWorld_PawnApparelGenerator_CanUseStuff"/> is worth
		/// memoising for this pawn.
		/// </summary>
		/// <remarks>
		/// Without specific apparel requirements or a faction stuff filter the vanilla check is a few
		/// field reads and a dictionary lookup would not pay for itself.
		/// </remarks>
		internal static bool IsApparelStuffMemoUseful(Pawn pawn)
		{
			if (pawn.kindDef != null && pawn.kindDef.specificApparelRequirements != null
				&& pawn.kindDef.specificApparelRequirements.Count > 0)
			{
				return true;
			}

			var faction = pawn.Faction;
			return faction != null
				&& !pawn.kindDef.ignoreFactionApparelStuffRequirements
				&& faction.def.apparelStuffFilter != null;
		}

		internal static bool TryGetApparelStuffUsable(Pawn pawn, ThingStuffPair pair, out bool value)
		{
			value = false;
			if (!_apparelSessionActive || pawn != _apparelPawn)
				return false;

			return _apparelStuffUsable.TryGetValue(pair, out value);
		}

		internal static void StoreApparelStuffUsable(Pawn pawn, ThingStuffPair pair, bool value)
		{
			if (!_apparelSessionActive || pawn != _apparelPawn)
				return;

			_apparelStuffUsable[pair] = value;
		}

		/// <summary>
		/// Returns the pawn's unmodified comfortable temperature range when an apparel session for the
		/// same pawn is open.
		/// </summary>
		/// <remarks>
		/// Vanilla subtracts the apparel insulation and adds the 10 degree safety margin after reading
		/// these two stats, so only the stat lookups themselves are cached. The values are captured on
		/// the first temperature check, which happens after <c>DestroyAll</c> has removed the pawn's old
		/// gear, and cannot change while generation runs.
		/// </remarks>
		internal static bool TryGetComfyTemperature(Pawn pawn, out float min, out float max)
		{
			min = 0f;
			max = 0f;
			if (!_apparelSessionActive || pawn != _apparelPawn)
				return false;

			if (!_apparelBaseTemperatureValid)
			{
				_apparelComfyTemperatureMin = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin,
					applyPostProcess: true, cacheStaleAfterTicks: 1);
				_apparelComfyTemperatureMax = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax,
					applyPostProcess: true, cacheStaleAfterTicks: 1);
				_apparelBaseTemperatureValid = true;
			}

			min = _apparelComfyTemperatureMin;
			max = _apparelComfyTemperatureMax;
			return true;
		}

		/// <summary>
		/// Returns the body part groups the pawn currently has when an apparel session for the same pawn
		/// is open.
		/// </summary>
		/// <remarks>
		/// Vanilla rebuilds the not-missing part enumeration for every group of every body part check.
		/// Body parts cannot change while a pawn's starting apparel is generated, so the union is
		/// computed once and answers every check in the call.
		/// </remarks>
		internal static bool TryGetWearableGroups(Pawn pawn, out HashSet<BodyPartGroupDef> groups)
		{
			groups = null;
			if (!_apparelSessionActive || pawn != _apparelPawn || pawn.health?.hediffSet == null)
				return false;

			if (!_apparelWearableGroupsValid)
			{
				if (_apparelWearableGroups == null)
					_apparelWearableGroups = new HashSet<BodyPartGroupDef>();
				else
					_apparelWearableGroups.Clear();

				foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
				{
					var partGroups = part.groups;
					for (var i = 0; i < partGroups.Count; i++)
						_apparelWearableGroups.Add(partGroups[i]);
				}

				_apparelWearableGroupsValid = true;
			}

			groups = _apparelWearableGroups;
			return true;
		}
	}
}
