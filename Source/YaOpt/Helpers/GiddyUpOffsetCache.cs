using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	///     Persists GiddyUp's draw-offset cache across launches.
	/// </summary>
	/// <remarks>
	///     GiddyUp computes a draw offset for every life stage of every PawnKindDef on each
	///     startup: it creates the body graphic, copies the texture through a RenderTexture
	///     readback and scans it pixel by pixel. The result lives in a session-only dictionary
	///     (ModSettings_GiddyUp.ExposeData does not scribe offsetCache), so the whole pass runs
	///     again on every launch. Seeding that dictionary from a per-entry checked file skips the
	///     work; entries whose graphic changed are dropped individually, so adding or removing an
	///     unrelated mod keeps the rest of the cache. The settings window's reset button still
	///     clears it, and the following ProcessPawnKinds call recomputes and overwrites the file.
	/// </remarks>
	internal static class GiddyUpOffsetCache
	{
		private const string HeaderPrefix = "#YaOpt GiddyUp offsets v1";
		private const string FileName = "YaOpt_GiddyUpOffsets.txt";

		private static FieldInfo offsetCacheField;
		private static string giddyUpVersion = string.Empty;
		private static bool seeded;
		private static bool saveFailureLogged;
		private static bool saveLogged;

		internal static bool Install(Harmony harmony)
		{
			try
			{
				var setup = AccessTools.TypeByName("GiddyUp.Setup");
				if (setup == null)
					return true;

				var process = AccessTools.Method(setup, "ProcessPawnKinds");
				var settings = AccessTools.TypeByName("GiddyUp.ModSettings_GiddyUp");
				offsetCacheField = settings == null ? null : AccessTools.Field(settings, "offsetCache");
				if (process == null || offsetCacheField == null)
				{
					YaOptMod.Warning(
						"GiddyUp detected but its ProcessPawnKinds/offsetCache was not found; the draw " +
						"offsets stay session-only.");
					return true;
				}

				giddyUpVersion = setup.Assembly.GetName().Version?.ToString() ?? string.Empty;
				harmony.Patch(process,
					prefix: new HarmonyMethod(typeof(GiddyUpOffsetCache), nameof(Prefix)),
					postfix: new HarmonyMethod(typeof(GiddyUpOffsetCache), nameof(Postfix)));
				YaOptMod.Log("GiddyUp detected; its draw-offset cache is persisted across launches.");
				return true;
			}
			catch (Exception exception)
			{
				// Never let this optional integration take down the rest of the patches.
				offsetCacheField = null;
				YaOptMod.Warning(
					$"Could not set up the GiddyUp draw-offset cache; it stays session-only:\n{exception}");
				return true;
			}
		}

		private static string CachePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

		private static void Prefix()
		{
			if (!YaOptGlobal.Settings.OptGiddyUpOffsetCache.Enabled)
				return;

			// Only the first call of a session (the Setup static constructor) seeds the cache. A later
			// call with a null dictionary means the settings window's reset button was used, and must
			// recompute instead of loading the old file again.
			if (seeded)
				return;

			seeded = true;
			try
			{
				Load();
			}
			catch (Exception exception)
			{
				YaOptMod.Warning($"Could not read the GiddyUp draw-offset cache:\n{exception}");
			}
		}

		private static void Postfix()
		{
			if (!YaOptGlobal.Settings.OptGiddyUpOffsetCache.Enabled)
				return;

			try
			{
				Save();
			}
			catch (Exception exception)
			{
				if (saveFailureLogged)
					return;

				saveFailureLogged = true;
				YaOptMod.Warning($"Could not write the GiddyUp draw-offset cache:\n{exception}");
			}
		}

		private static void Load()
		{
			var path = CachePath;
			if (!File.Exists(path))
			{
				YaOptMod.Log("No cached GiddyUp draw offsets yet; they will be written after this pass.");
				return;
			}

			using var reader = new StreamReader(path);
			var header = reader.ReadLine();
			if (header == null || header != HeaderPrefix)
			{
				YaOptMod.Log("GiddyUp draw-offset cache format changed; rebuilding it once.");
				return;
			}

			var current = BuildCurrentHashes();
			var cache = GetOrCreateCache();
			var loaded = 0;
			var stale = 0;
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				var parts = line.Split('\t');
				if (parts.Length != 3 ||
					!ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hash) ||
					!current.TryGetValue(parts[0], out var expected) || expected != hash ||
					!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				{
					stale++;
					continue;
				}

				cache[parts[0]] = value;
				loaded++;
			}

			YaOptMod.Log(stale == 0
				? $"Loaded {loaded} cached GiddyUp draw offsets."
				: $"Loaded {loaded} cached GiddyUp draw offsets; {stale} stale entries will be recomputed.");
		}

		private static void Save()
		{
			var cache = offsetCacheField?.GetValue(null) as Dictionary<string, float>;
			if (cache == null || cache.Count == 0)
				return;

			var path = CachePath;
			var temp = path + ".tmp";
			try
			{
				var current = BuildCurrentHashes();
				var written = 0;
				using (var writer = new StreamWriter(temp))
				{
					writer.WriteLine(HeaderPrefix);
					foreach (var pair in cache)
					{
						if (!current.TryGetValue(pair.Key, out var hash))
						{
							// The def no longer exists; drop it instead of carrying it forever.
							continue;
						}

						writer.Write(pair.Key);
						writer.Write('\t');
						writer.Write(pair.Value.ToString("R", CultureInfo.InvariantCulture));
						writer.Write('\t');
						writer.WriteLine(hash.ToString("X16", CultureInfo.InvariantCulture));
						written++;
					}
				}

				File.Move(temp, path, overwrite: true);
				if (saveLogged)
					return;

				saveLogged = true;
				YaOptMod.Log($"Wrote {written} GiddyUp draw offsets for the next launch.");
			}
			finally
			{
				// A failed move leaves the temporary file behind; never let it accumulate.
				try
				{
					if (File.Exists(temp))
						File.Delete(temp);
				}
				catch
				{
					// Best effort only.
				}
			}
		}

		private static Dictionary<string, float> GetOrCreateCache()
		{
			if (offsetCacheField?.GetValue(null) is Dictionary<string, float> existing)
				return existing;

			var cache = new Dictionary<string, float>();
			offsetCacheField?.SetValue(null, cache);
			return cache;
		}

		/// <summary>
		///     Per-entry fingerprint of everything SetDrawOffset reads: the pawn kind/life stage, the
		///     body graphic path, its draw size and the GiddyUp version whose formula produced the
		///     value. Only the entries whose own inputs changed are dropped.
		/// </summary>
		private static Dictionary<string, ulong> BuildCurrentHashes()
		{
			var hashes = new Dictionary<string, ulong>();
			var kinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
			for (var i = 0; i < kinds.Count; i++)
			{
				var kind = kinds[i];
				var stages = kind.lifeStages;
				if (stages == null)
					continue;

				for (var j = 0; j < stages.Count; j++)
				{
					// Same key TextureUtility.FormatKey builds.
					var key = (kind.defName ?? string.Empty) + "/" + j;
					var graphic = stages[j]?.bodyGraphicData;
					var hash = 14695981039346656037UL;
					AddString(ref hash, giddyUpVersion);
					AddString(ref hash, key);
					AddString(ref hash, graphic?.texPath ?? string.Empty);
					AddInt(ref hash, graphic == null ? 0 : BitConverter.SingleToInt32Bits(graphic.drawSize.x));
					AddInt(ref hash, graphic == null ? 0 : BitConverter.SingleToInt32Bits(graphic.drawSize.y));
					hashes[key] = hash;
				}
			}

			return hashes;
		}

		private static void AddString(ref ulong hash, string value)
		{
			for (var i = 0; i < value.Length; i++)
				hash = (hash ^ value[i]) * 1099511628211UL;

			// Separator so "ab" + "c" and "a" + "bc" hash differently.
			hash = (hash ^ 0xFFFF) * 1099511628211UL;
		}

		private static void AddInt(ref ulong hash, int value)
		{
			hash = (hash ^ (uint)value) * 1099511628211UL;
		}
	}
}