using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches.Compatibility.HumanoidAlienRaces
{
	/// <summary>
	/// Skips Humanoid Alien Races' race check when vanilla already decided the pawn cannot wear the
	/// apparel.
	/// </summary>
	/// <remarks>
	/// HAR postfixes <c>ApparelProperties.PawnCanWear</c> with
	/// <c>__result &amp;= RaceRestrictionSettings.CanWear(...)</c>. Once <c>__result</c> is false the race
	/// query cannot change the outcome but still runs for every candidate pair. The prefix only short
	/// circuits that false case and returns the unchanged result otherwise.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearHarFastPath"/>
	[HarmonyPatch]
	internal static class AlienRace_HarmonyPatches_PawnCanWearPostfix
	{
		private static MethodBase _target;

		internal static MethodBase Target => _target ??= Resolve();

		static MethodBase TargetMethod()
		{
			return Target;
		}

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptPawnGearHarFastPath.Enabled == true && Target != null;
		}

		private static MethodBase Resolve()
		{
			try
			{
				var type = AccessTools.TypeByName("AlienRace.HarmonyPatches");
				var method = type == null ? null : AccessTools.Method(type, "PawnCanWearPostfix");
				if (method == null)
					return null;

				// The optimisation reads the postfix's result argument, so the shape has to match.
				var parameters = method.GetParameters();
				if (parameters.Length != 3
					|| parameters[0].ParameterType != typeof(ApparelProperties)
					|| parameters[1].ParameterType != typeof(Pawn)
					|| parameters[2].ParameterType != typeof(bool).MakeByRefType())
				{
					return null;
				}

				return method;
			}
			catch (System.Exception)
			{
				return null;
			}
		}

		static bool Prefix(ref bool __2)
		{
			return __2;
		}
	}
}