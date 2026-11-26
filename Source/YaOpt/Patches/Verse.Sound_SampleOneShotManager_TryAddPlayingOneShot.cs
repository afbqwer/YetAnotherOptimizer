using HarmonyLib;
using Verse.Sound;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Replaces the LINQ voice count in <see cref="SampleOneShotManager.TryAddPlayingOneShot"/> with a direct scan.
	/// </summary>
	/// <remarks>
	/// Vanilla allocates a closure and an iterator to count samples that share the sub-sound or its tag, on
	/// every one-shot that starts. The eviction path itself is kept: it still calls
	/// <c>LeastImportantOf(SubSoundDef)</c> and removes exactly one sample, so voice selection and its
	/// tie-breaking are unchanged.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptSoundOneShotManager"/>
	[HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.TryAddPlayingOneShot))]
	internal static class Verse_Sound_SampleOneShotManager_TryAddPlayingOneShot
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptSoundOneShotManager.Enabled != true)
				return false;

			return !PatchConflictGuard.HasForeignConflict(
				AccessTools.Method(typeof(SampleOneShotManager), nameof(SampleOneShotManager.TryAddPlayingOneShot)));
		}

		static bool Prefix(SampleOneShotManager __instance, SampleOneShot newSample)
		{
			var samples = __instance.samples;
			var subDef = newSample.subDef;

			var count = 0;
			for (var i = 0; i < samples.Count; i++)
			{
				if (samples[i].subDef.IsSameOrHasSameTag(subDef))
					count++;
			}

			if (count >= subDef.parentDef.maxVoices)
			{
				var least = __instance.LeastImportantOf(subDef);
				least.source.Stop();
				samples.Remove(least);
			}

			samples.Add(newSample);
			return false;
		}
	}
}