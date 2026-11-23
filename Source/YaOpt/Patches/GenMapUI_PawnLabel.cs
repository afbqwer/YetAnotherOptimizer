using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Caches <see cref="GenMapUI.GetPawnLabel"/> results for a short period.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptPawnLabelCache"/>
	[HarmonyPatch(typeof(GenMapUI))]
	[HarmonyPatch("GetPawnLabel")]
	internal static class GenMapUI_GetPawnLabel
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPawnLabelCache.Enabled;
		}

		static bool Prefix(Pawn pawn, float truncateToWidth,
			Dictionary<string, string> truncatedLabelsCache, GameFont font, ref string __result)
		{
			var frame = Time.frameCount;
			if (!PawnLabelCacheStore.IsCacheable(pawn) || frame <= 0)
				return true;

			var entry = PawnLabelCacheStore.GetEntry(frame,
				new PawnLabelCacheStore.Key(pawn, font, truncateToWidth));
			if (entry.HasLabel)
			{
				__result = entry.Label;
				return false;
			}

			var previous = Text.Font;
			Text.Font = font;
			try
			{
				entry.Label = pawn.LabelShortCap.Truncate(truncateToWidth, truncatedLabelsCache);
			}
			finally
			{
				Text.Font = previous;
			}

			entry.HasLabel = true;
			__result = entry.Label;
			return false;
		}
	}

	/// <summary>
	/// Caches <see cref="GenMapUI.GetPawnLabelNameWidth"/> results for a short period.
	/// </summary>
	/// <seealso cref="YaOptSettings.OptPawnLabelCache"/>
	[HarmonyPatch(typeof(GenMapUI))]
	[HarmonyPatch("GetPawnLabelNameWidth")]
	internal static class GenMapUI_GetPawnLabelNameWidth
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptPawnLabelCache.Enabled;
		}

		static bool Prefix(Pawn pawn, float truncateToWidth,
			Dictionary<string, string> truncatedLabelsCache, GameFont font, ref float __result)
		{
			var frame = Time.frameCount;
			if (!PawnLabelCacheStore.IsCacheable(pawn) || frame <= 0)
				return true;

			var entry = PawnLabelCacheStore.GetEntry(frame,
				new PawnLabelCacheStore.Key(pawn, font, truncateToWidth));
			if (entry.HasWidth)
			{
				__result = entry.Width;
				return false;
			}

			var previous = Text.Font;
			Text.Font = font;
			try
			{
				if (!entry.HasLabel)
				{
					entry.Label = pawn.LabelShortCap.Truncate(truncateToWidth, truncatedLabelsCache);
					entry.HasLabel = true;
				}

				var width = font != GameFont.Tiny
					? Text.CalcSize(entry.Label).x
					: entry.Label.GetWidthCached();

				if (Math.Abs(Math.Round(Prefs.UIScale) - (double)Prefs.UIScale) > float.Epsilon)
					width += 0.5f;

				if (width < 20f)
					width = 20f;

				entry.Width = width;
				entry.HasWidth = true;
			}
			finally
			{
				Text.Font = previous;
			}

			__result = entry.Width;
			return false;
		}
	}
}