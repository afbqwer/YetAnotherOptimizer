using System;
using HarmonyLib;
using YaOpt.Helpers;

namespace YaOpt.Patches.Compatibility.HumanoidAlienRaces
{
	/// <summary>
	/// Disables Humanoid Alien Races' gear pool filtering while the constructive rewrite is active.
	/// </summary>
	/// <remarks>
	/// HAR removes restricted pairs from the shared pools in its generation prefixes and appends them
	/// back from its postfix/cleanup. The rewrite replaces both generation methods and does the filtering
	/// through <see cref="PawnGearPools"/> instead, so every HAR gear patch method has to be neutralised:
	/// leaving a prefix or restore half active would corrupt the shared pools. All four methods are found
	/// before the option is enabled; the runtime flag is shared with the rewrite so turning the option off
	/// immediately restores HAR's own behaviour.
	/// </remarks>
	/// <seealso cref="YaOpt.YaOptSettings.OptPawnGearRebuild"/>
	/// <seealso cref="PawnGearRebuildCompat"/>
	[ManualPatch]
	internal static class AlienRace_PawnGearNeutralizer
	{
		internal static bool Patch(Harmony harmony)
		{
			if (!PawnGearRebuildCompat.Ready)
				return true;

			var methods = PawnGearRebuildCompat.HarPatchMethods;
			if (methods == null || methods.Length == 0)
				return true;

			var prefix = new HarmonyMethod(typeof(AlienRace_PawnGearNeutralizer), nameof(Neutralize));
			var noError = true;
			for (var i = 0; i < methods.Length; i++)
			{
				try
				{
					harmony.Patch(methods[i], prefix: prefix);
				}
				catch (Exception ex)
				{
					noError = false;
					YaOptMod.Error(ex.ToString());
				}
			}

			return noError;
		}

		private static bool Neutralize()
		{
			return !PawnGearRebuildCompat.Active;
		}
	}
}