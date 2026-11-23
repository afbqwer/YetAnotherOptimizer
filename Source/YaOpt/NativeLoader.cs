using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Burst;
using Verse;

namespace YaOpt
{
	public static class NativeLoader
	{
		public const string LOAD_FOLDER = "1.6";

		public static void LoadLibraries(ModContentPack content)
		{
#if RIMWORLD_1_7
			// Unity Burst is a managed stub in RimWorld 1.7: IsEnabled is always false and
			// LoadAdditionalLibrary is a no-op. Do not load the 1.6 native libraries.
			YaOptGlobal.IsBurstAvailable = false;
			YaOptMod.Debug("Burst features are unavailable on RimWorld 1.7.");
			return;
#else
			string burstDllName = null;

			if (Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					burstDllName = "yaopt_burst_win64.dll";
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					burstDllName = "yaopt_burst_linux64.so";
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					burstDllName = "yaopt_burst_osx64.bundle";
				}
			}

			if (burstDllName != null)
			{
				LoadBurstLibrary(content, burstDllName);
			}
			else
			{
				YaOptMod.Warning($"This system doesn't support Burst. Current OS: {Environment.OSVersion}");
			}
		}

		private static string GetFileFromFolders(List<string> folders, string filename)
		{
			foreach (var folder in folders)
			{
				var path = Path.Combine(folder, filename);
				if (File.Exists(path))
					return path;
			}
			return null;
		}

		private static void LoadBurstLibrary(ModContentPack content, string dllName)
		{
			try
			{
				var dllPath = GetFileFromFolders(content.foldersToLoadDescendingOrder,
					Path.Combine("Burst", dllName));
				if (string.IsNullOrEmpty(dllPath))
				{
					throw new FileNotFoundException("Cannot find Burst library", dllPath);
				}
				YaOptMod.Debug($"Load Burst library from {dllPath}");
				if (BurstRuntime.LoadAdditionalLibrary(dllPath))
				{
					YaOptGlobal.IsBurstAvailable = true;
				}
				else
				{
					YaOptMod.Error($"Failed to load Burst library from {dllPath}. Any features that require Burst will not work.");
				}
			}
			catch (Exception e)
			{
				YaOptMod.Error($"Error when load Burst library. " +
							   $"Any features that require Burst will not work.\n {e}");
			}
#endif
		}
	}
}
