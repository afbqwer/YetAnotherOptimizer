using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the LINQ count in <see cref="SampleOneShotManager.CanAddPlayingOneShot"/> with a direct scan.
	/// </summary>
	/// <remarks>
	/// The vanilla predicate runs <c>Where(...).Count()</c>, allocating a closure and an iterator for every
	/// one-shot the game tries to play, and keeps enumerating after the limit is already exceeded. The scan
	/// below reads <c>Time.frameCount</c> once and stops at the limit. The decision is unchanged, including
	/// the <c>MaxSimultaneousSamples &lt;= 0</c> case where vanilla always rejects.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptSoundOneShotManager"/>
	[HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.CanAddPlayingOneShot))]
	internal static class Verse_Sound_SampleOneShotManager_CanAddPlayingOneShot
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptSoundOneShotManager.Enabled != true)
				return false;

			return AccessTools.Method(typeof(SampleOneShotManager),
				nameof(SampleOneShotManager.CanAddPlayingOneShot)) != null;
		}

		static bool Prefix(SampleOneShotManager __instance, SoundDef def, SoundInfo info, ref bool __result)
		{
			if (!SoundDefHelper.CorrectContextNow(def, info.Maker.Map))
			{
				__result = false;
				return false;
			}

			var max = def.MaxSimultaneousSamples;
			if (max <= 0)
			{
				__result = false;
				return false;
			}

			var samples = __instance.samples;
			var frame = Time.frameCount;
			var count = 0;
			for (var i = 0; i < samples.Count; i++)
			{
				var sample = samples[i];
				if (sample.subDef.parentDef != def || (sample.AgeRealTime >= 0.05f && sample.startFrame != frame))
					continue;

				if (++count >= max)
				{
					__result = false;
					return false;
				}
			}

			__result = true;
			return false;
		}
	}
}