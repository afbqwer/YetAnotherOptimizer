using Verse;

namespace YaOpt.Helpers.Pathfinding
{
	/// <summary>
	/// Caches <see cref="Pawn.TicksPerMove"/> for the current game tick.
	/// </summary>
	/// <remarks>
	/// Vanilla evaluates <c>TicksPerMove</c> both when a path request is parameterized and again for every
	/// cell a pawn walks into, so the same pawn is re-evaluated dozens of times per tick. The inputs
	/// (MoveSpeed — itself already cached by <c>OptMoveSpeedStatCache</c> — restraints, carried pawns,
	/// weather multiplier) change between ticks, not within one, so a one-tick memo is safe. Two slots keep
	/// the cardinal/diagonal pair warm; the cache is <see cref="System.ThreadStaticAttribute"/> because
	/// only the main thread advances pawns.
	/// </remarks>
	internal static class TicksPerMoveCache
	{
		[System.ThreadStatic] private static Pawn _cardinalPawn;
		[System.ThreadStatic] private static int _cardinalTick = -1;
		[System.ThreadStatic] private static float _cardinalValue;

		[System.ThreadStatic] private static Pawn _diagonalPawn;
		[System.ThreadStatic] private static int _diagonalTick = -1;
		[System.ThreadStatic] private static float _diagonalValue;

		internal static bool TryGet(Pawn pawn, bool diagonal, out float value)
		{
			var tick = GenTicks.TicksGame;
			if (diagonal)
			{
				if (ReferenceEquals(pawn, _diagonalPawn) && tick == _diagonalTick)
				{
					value = _diagonalValue;
					return true;
				}
			}
			else if (ReferenceEquals(pawn, _cardinalPawn) && tick == _cardinalTick)
			{
				value = _cardinalValue;
				return true;
			}

			value = 0f;
			return false;
		}

		internal static void Store(Pawn pawn, bool diagonal, float value)
		{
			var tick = GenTicks.TicksGame;
			if (diagonal)
			{
				_diagonalPawn = pawn;
				_diagonalTick = tick;
				_diagonalValue = value;
			}
			else
			{
				_cardinalPawn = pawn;
				_cardinalTick = tick;
				_cardinalValue = value;
			}
		}
	}
}