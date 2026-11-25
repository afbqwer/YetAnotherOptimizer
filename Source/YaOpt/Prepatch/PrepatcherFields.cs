using Prepatcher;
using Verse;
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