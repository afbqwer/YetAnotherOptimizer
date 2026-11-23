using System;
using System.Collections.Generic;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Short-lived cache for <see cref="GenMapUI"/> pawn labels and their widths.
	/// </summary>
	/// <remarks>
	/// Vanilla rebuilds <c>Pawn.LabelShortCap</c> and measures it at least twice per rendered pawn per frame.
	/// Labels only change on rename or graphic changes, so a 60-frame TTL is imperceptible.
	/// </remarks>
	internal static class PawnLabelCacheStore
	{
		internal const int TtlFrames = 60;

		private const int MaxEntries = 2048;

		internal readonly struct Key : IEquatable<Key>
		{
			private readonly int pawnId;
			private readonly int font;
			private readonly float truncateToWidth;

			internal Key(Pawn pawn, GameFont font, float truncateToWidth)
			{
				pawnId = pawn.thingIDNumber;
				this.font = (int)font;
				this.truncateToWidth = truncateToWidth;
			}

			public bool Equals(Key other)
			{
				return pawnId == other.pawnId &&
					   font == other.font &&
					   truncateToWidth.Equals(other.truncateToWidth);
			}

			public override bool Equals(object obj)
			{
				return obj is Key other && Equals(other);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					var hash = pawnId * 397;
					hash = hash * 31 + font;
					return hash * 31 + truncateToWidth.GetHashCode();
				}
			}
		}

		internal sealed class Entry
		{
			internal string Label = string.Empty;

			internal bool HasLabel;

			internal float Width;

			internal bool HasWidth;

			internal int Frame;
		}

		private static readonly Dictionary<Key, Entry> Cache = new Dictionary<Key, Entry>();

		static PawnLabelCacheStore()
		{
			UpdateCallbackHelper.RegisterClearCacheCallback(Clear);
		}

		internal static Entry GetEntry(int frame, in Key key)
		{
			if (Cache.TryGetValue(key, out var entry) && frame - entry.Frame <= TtlFrames)
				return entry;

			if (Cache.Count >= MaxEntries)
				Cache.Clear();

			entry = new Entry { Frame = frame };
			Cache[key] = entry;
			return entry;
		}

		internal static bool IsCacheable(Pawn pawn)
		{
			return pawn != null && pawn.thingIDNumber >= 0;
		}

		internal static void Clear()
		{
			Cache.Clear();
		}
	}
}