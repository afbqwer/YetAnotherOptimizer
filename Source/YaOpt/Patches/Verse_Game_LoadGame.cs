using HarmonyLib;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	[HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
	internal static class Verse_Game_LoadGame
	{
		static void Prefix()
		{
			DeathThoughtsHelper.BeginLoadGame();
		}

		static void Postfix()
		{
			DeathThoughtsHelper.CompleteLoadGame();
		}

		static void Finalizer()
		{
			DeathThoughtsHelper.AbortLoadGame();
		}
	}
}
