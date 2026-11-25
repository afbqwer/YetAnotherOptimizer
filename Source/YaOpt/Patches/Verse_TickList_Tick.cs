using HarmonyLib;
using System.Collections.Generic;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Uses indexed swap-removal for things removed from a tick list.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptFastListerRemove"/>
	[HarmonyPatch(typeof(TickList))]
	[HarmonyPatch(nameof(TickList.Tick))]
	internal static class Verse_TickList_Tick
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptFastListerRemove.Enabled;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (var instruction in instructions)
			{
				if (instruction.Calls("Remove"))
				{
					yield return CodeInstruction.Call(
						typeof(MiscHelper),
						nameof(MiscHelper.ReverseRemove), null, new[] { typeof(Thing) });
					continue;
				}
				yield return instruction;
			}
		}
	}
}
