using HarmonyLib;
using Verse;
using Verse.Sound;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Drops finished one-shots in place instead of rebuilding the list through a second list lookup.
	/// </summary>
	/// <remarks>
	/// Vanilla collects finished samples in <c>cleanupList</c> and then calls
	/// <c>samples.RemoveAll(s =&gt; cleanupList.Contains(s))</c>, which is O(n*m) and allocates a closure
	/// every frame. The compaction below runs the same Update pass, evaluates the same removal condition
	/// in the same order, stops the same sources, and keeps the surviving samples in their original order.
	/// The unused <c>cleanupList</c> field is left untouched.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptSoundOneShotManager"/>
	[HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.SampleOneShotManagerUpdate))]
	internal static class Verse_Sound_SampleOneShotManager_SampleOneShotManagerUpdate
	{
		static bool Prepare()
		{
			if (YaOptGlobal.Settings?.OptSoundOneShotManager.Enabled != true)
				return false;

			return !PatchConflictGuard.HasForeignConflict(
				AccessTools.Method(typeof(SampleOneShotManager), nameof(SampleOneShotManager.SampleOneShotManagerUpdate)));
		}

		static bool Prefix(SampleOneShotManager __instance)
		{
			var samples = __instance.samples;
			for (var i = 0; i < samples.Count; i++)
				samples[i].Update();

			var write = 0;
			for (var read = 0; read < samples.Count; read++)
			{
				var sample = samples[read];
				var finished = sample.source == null ||
							   (!sample.source.isPlaying &&
								(!sample.subDef.tempoAffectedByGameSpeed || !Find.TickManager.Paused)) ||
							   !SoundDefHelper.CorrectContextNow(sample.subDef.parentDef, sample.Map);
				if (finished)
				{
					if (sample.source != null && sample.source.isPlaying)
						sample.source.Stop();
					sample.SampleCleanup();
					continue;
				}

				if (write != read)
					samples[write] = sample;
				write++;
			}

			if (write < samples.Count)
				samples.RemoveRange(write, samples.Count - write);
			return false;
		}
	}
}