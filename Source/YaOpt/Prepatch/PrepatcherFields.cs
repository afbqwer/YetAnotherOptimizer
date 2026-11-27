using Prepatcher;
using RimWorld;
using Verse;
using Verse.AI;
using YaOpt.Helpers;

namespace YaOpt.Prepatch
{
	/// <summary>
	/// Injected per-instance cache fields for vanilla types.
	/// </summary>
	/// <remarks>
	/// The built-in PrepatcherCompat runs injection before mod constructors, so the fields exist
	/// for every instance created afterwards. Accessors are rewritten into direct field access.
	/// </remarks>
	public static class PrepatcherFields
	{
		[PrepatcherField]
		[ValueInitializer(nameof(CreateListerState))]
		public static extern ref ListerBuildingsCache.State Lister(this ListerBuildings target);

		[PrepatcherField]
		[ValueInitializer(nameof(CreateImmunityState))]
		public static extern ref ImmunityCache.State Immunity(this ImmunityHandler target);

		[PrepatcherField]
		[ValueInitializer(nameof(CreateRefuelState))]
		public static extern ref RefuelWorkCandidates.State RefuelCandidates(this Map target);

		/// <summary>
		/// Packed invisibility state. Zero means "no invisibility comp", which is also the correct
		/// answer before the first publish, so a plain default is enough and no initializer is
		/// needed. See <see cref="YaOpt.Helpers.PawnInvisibilityCache"/> for the bit layout.
		/// </summary>
		[PrepatcherField]
		public static extern ref long Invisibility(this Pawn target);

		[PrepatcherField]
		public static extern ref bool ShamblerState(this Pawn target);

		/// <summary>
		/// Per-driver bookkeeping for <see cref="ToilRecursionGuard"/>. All fields are counters for
		/// the current tick, so a zero default is correct and no initializer is needed.
		/// </summary>
		[PrepatcherField]
		public static extern ref ToilRecursionGuard.State ToilGuard(this JobDriver target);

		/// <summary>
		/// Draw parameters used by the parallel material preparation, so the draw phase can tell
		/// whether this node's block was filled for the current request.
		/// </summary>
		[PrepatcherField]
		public static extern ref MaterialPrepareState PreparedMaterial(this PawnRenderNode target);

		/// <summary>
		/// Cached draw inputs for dead-pawn render preparation reuse.
		/// </summary>
		[PrepatcherField]
		public static extern ref CorpseRenderState CorpseRender(this PawnRenderTree target);

		public static ListerBuildingsCache.State CreateListerState()
		{
			return new ListerBuildingsCache.State();
		}

		public static ImmunityCache.State CreateImmunityState()
		{
			return new ImmunityCache.State();
		}

		public static RefuelWorkCandidates.State CreateRefuelState()
		{
			return new RefuelWorkCandidates.State();
		}
	}
}