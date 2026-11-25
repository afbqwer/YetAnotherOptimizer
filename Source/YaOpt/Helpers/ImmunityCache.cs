using HarmonyLib;
using System.Collections.Generic;
using Verse;
using YaOpt.Prepatch;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Caches <see cref="ImmunityHandler"/>'s needed-immunity list until the hediff list changes.
	/// </summary>
	/// <remarks>
	/// Vanilla rebuilds this list on every health tick for every pawn. The list only depends on the
	/// <see cref="HediffDef"/>s in <c>hediffSet.hediffs</c>, so the list identity plus
	/// <c>List&lt;T&gt;._version</c> is enough to detect every relevant change.
	/// </remarks>
	public static class ImmunityCache
	{
		public sealed class State
		{
			public List<Hediff> Hediffs;

			public int Version;

			public readonly List<ImmunityHandler.ImmunityInfo> Infos =
				new List<ImmunityHandler.ImmunityInfo>(4);
		}

		private static readonly AccessTools.FieldRef<List<Hediff>, int> VersionField =
			AccessTools.FieldRefAccess<int>(typeof(List<Hediff>), "_version");

		public static List<ImmunityHandler.ImmunityInfo> NeededImmunitiesNow(ImmunityHandler handler)
		{
			var hediffs = handler.pawn.health.hediffSet.hediffs;
			if (!PrepatcherState.ImmunityActive)
				return ComputeVanilla(hediffs);

			ref var state = ref PrepatcherFields.Immunity(handler);
			if (state == null)
				state = new State();

			var version = VersionField(hediffs);
			if (!ReferenceEquals(state.Hediffs, hediffs) || state.Version != version)
			{
				state.Hediffs = hediffs;
				state.Version = version;
				Fill(state.Infos, hediffs);
			}
			return state.Infos;
		}

		private static List<ImmunityHandler.ImmunityInfo> ComputeVanilla(List<Hediff> hediffs)
		{
			var result = ImmunityHandler.tmpNeededImmunitiesNow;
			Fill(result, hediffs);
			return result;
		}

		private static void Fill(List<ImmunityHandler.ImmunityInfo> result, List<Hediff> hediffs)
		{
			result.Clear();
			for (var i = 0; i < hediffs.Count; i++)
			{
				var hediff = hediffs[i];
				if (!hediff.def.PossibleToDevelopImmunityNaturally())
					continue;

				result.Add(new ImmunityHandler.ImmunityInfo
				{
					immunity = hediff.def,
					source = hediff.def
				});
			}
		}
	}
}