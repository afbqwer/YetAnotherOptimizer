using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Per-node record of the draw parameters used to prepare its material property block.
	/// </summary>
	public struct MaterialPrepareState
	{
		private int _frame;
		private Material _material;
		private Color _tint;
		private Color _statueColor;
		private Faction _faction;
		private bool _statue;

		public void Set(int frame, Material material, PawnDrawParms parms)
		{
			_frame = frame;
			_material = material;
			_tint = parms.tint;
			_statue = parms.Statue;
			_statueColor = parms.statueColor.GetValueOrDefault();
			_faction = parms.pawn?.Faction;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Matches(int frame, Material material, PawnDrawParms parms)
		{
			if (_frame != frame || !ReferenceEquals(_material, material) || _statue != parms.Statue ||
				!ReferenceEquals(_faction, parms.pawn?.Faction))
				return false;

			return _statue ? _statueColor == parms.statueColor.GetValueOrDefault() : _tint == parms.tint;
		}

		public void Clear()
		{
			_frame = 0;
			_material = null;
			_faction = null;
		}
	}
}