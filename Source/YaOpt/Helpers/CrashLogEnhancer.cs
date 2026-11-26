using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Verse;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Managed half of the crash report. The native VEH shim records the fault site; this class
	/// records the managed exception, optionally keeps the runtime's stderr text, and surfaces the
	/// previous run's report at startup.
	/// </summary>
	/// <remarks>
	/// Everything lands in the same YaOpt_Crash_&lt;pid&gt;.log as the native VEH records. The native
	/// side opens it with FILE_APPEND_DATA and FILE_SHARE_WRITE, so the CRT stderr stream and this
	/// managed writer can append without a sharing violation and without overwriting each other.
	/// Reports live in the Logs subfolder of the save data folder, next to Player.log, and retention
	/// mirrors the game's own Player.log / Player-prev.log scheme: only the current session's
	/// report and the newest previous report survive a launch.
	/// </remarks>
	public static class CrashLogEnhancer
	{
		private const string FilePrefix = "YaOpt_Crash_";
		private const string LogSuffix = ".log";
		private const string PrevName = "YaOpt_Crash-prev.log";
		private const string LogFolderName = "Logs";
		private const string GppPackageId = "wowgag.guiperformancepatch";
		private const int PreviewChars = 6000;
		private const int Unbuffered = 4;
		private const uint FileTypeDisk = 1u;
		private const uint FileTypeChar = 2u;
		private const uint FileAppendData = 0x4u;
		private const uint FileShareAll = 0x7u;
		private const uint OpenAlways = 4u;
		private const uint FileAttributeNormal = 0x80u;

		private static string _sharedLogPath;

		/// <summary>
		/// Directory that holds this session's report and the archived previous report. Falls back
		/// to the save data folder root when the subfolder cannot be created, so the reporter never
		/// silently stops working.
		/// </summary>
		internal static string LogFolder
		{
			get
			{
				var root = GenFilePaths.SaveDataFolderPath;
				try
				{
					var dir = Path.Combine(root, LogFolderName);
					Directory.CreateDirectory(dir);
					return dir;
				}
				catch (Exception e)
				{
					YaOptMod.Warning("Could not create the crash log folder under " + root + ": " +
									 e.GetType().Name);
					return root;
				}
			}
		}

		internal static string SessionLogPath =>
			Path.Combine(LogFolder, $"{FilePrefix}{Environment.ProcessId}{LogSuffix}");

		/// <summary>
		/// True when at least one feature writes the crash log. With all of them off the folder is
		/// left untouched, including any reports an earlier configuration produced.
		/// </summary>
		private static bool CrashLoggingActive =>
			YaOptGlobal.Settings.VectoredExceptionHandler.Enabled ||
			YaOptGlobal.Settings.EnhancedCrashLog.Enabled ||
			YaOptGlobal.Settings.FatalStderrCapture.Enabled;

		/// <summary>
		/// Archives the newest previous run and prunes the rest before the native reporter installs
		/// its log with CREATE_ALWAYS, which would otherwise truncate a reused-PID report first.
		/// </summary>
		public static void Prepare()
		{
			try
			{
				var dir = LogFolder;
				_sharedLogPath = Path.Combine(dir, $"{FilePrefix}{Environment.ProcessId}{LogSuffix}");
				if (CrashLoggingActive)
					SummarizePrevious(dir);
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Failed to summarize the previous crash log. " +
								 "This does not affect gameplay.\n" + e);
			}
		}

		/// <summary>
		/// Redirects stderr only after the native install, because that install opens the shared
		/// log with CREATE_ALWAYS and would wipe the first redirected records.
		/// </summary>
		public static void Init()
		{
			try
			{
				if (string.IsNullOrEmpty(_sharedLogPath))
					_sharedLogPath = SessionLogPath;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
					YaOptGlobal.Settings.FatalStderrCapture.Enabled)
				{
					InstallStderrCapture();
				}
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Failed to initialize the managed crash log. " +
								 "This does not affect gameplay.\n" + e);
			}
		}

		/// <summary>
		/// Appends the managed exception and its full stack to the shared crash log. Called from
		/// DebugHelper.OnUnhandledException, which still mirrors the report to the game log.
		/// </summary>
		public static void WriteManagedException(object exceptionObject)
		{
			if (string.IsNullOrEmpty(_sharedLogPath) || !YaOptGlobal.Settings.EnhancedCrashLog.Enabled)
				return;

			var sb = new StringBuilder()
				.AppendLine("=== Unhandled managed exception ===")
				.AppendLine(DateTime.Now.ToString("MM-dd HH:mm:ss"))
				.AppendLine(exceptionObject?.ToString() ?? "Unknown exception object");

			if (exceptionObject is Exception ex)
			{
				var stackTrace = new StackTrace(ex, true);
				foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
				{
					var method = frame.GetMethod();
					var name = method == null
						? "?"
						: (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
					sb.Append("  at ").Append(name);
					var file = frame.GetFileName();
					if (!string.IsNullOrEmpty(file))
						sb.Append(" in ").Append(file).Append(':').Append(frame.GetFileLineNumber());
					sb.AppendLine();
				}
			}

			sb.AppendLine("===================================");
			Append(_sharedLogPath, sb.ToString());
		}

		/// <summary>
		/// Removes the shared log when neither a native record, an exception nor stderr output ever reached it.
		/// </summary>
		public static void TryDeleteEmptySharedLog()
		{
			try
			{
				if (string.IsNullOrEmpty(_sharedLogPath))
					return;
				var info = new FileInfo(_sharedLogPath);
				if (info.Exists && info.Length == 0)
					info.Delete();
			}
			catch (Exception)
			{
				// Process is exiting, and the CRT may still hold the handle. Nothing useful to do.
			}
		}

		/// <summary>
		/// Copies the newest previous crash report to YaOpt_Crash-prev.log and deletes every other
		/// report, so the folder only ever holds this session's log plus that one archive. The copy
		/// is written before anything is deleted; if it cannot be written, its source files are kept.
		/// </summary>
		private static void SummarizePrevious(string dir)
		{
			var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

			foreach (var file in Directory.GetFiles(dir, FilePrefix + "*"))
			{
				var name = Path.GetFileName(file);
				if (!TryGetPid(name, out var pid))
					continue;

				if (!name.EndsWith(LogSuffix, StringComparison.Ordinal))
				{
					// "....log.seen[.ticks]" renamed by older builds. The archive is rebuilt from
					// the plain ".log" files, so these leftovers are prune-only.
					if (name.IndexOf(".log.seen", StringComparison.Ordinal) >= 0)
						TryDeleteFile(file);
					continue;
				}

				if (new FileInfo(file).Length == 0)
				{
					TryDeleteFile(file);
					continue;
				}

				if (!groups.TryGetValue(pid, out var list))
					groups[pid] = list = new List<string>();
				list.Add(file);
			}

			if (groups.Count == 0)
				return;

			var newestPid = groups.Keys
				.OrderByDescending(pid => groups[pid].Max(file => new FileInfo(file).LastWriteTime))
				.First();

			var prevPath = Path.Combine(dir, PrevName);
			var archived = false;
			try
			{
				// Streamed rather than buffered whole: a stack overflow's stderr tail can be large,
				// and the full text is kept so deleting the originals loses nothing.
				using (var writer = new StreamWriter(prevPath, false, new UTF8Encoding(false)))
				{
					writer.WriteLine("=== YaOpt previous crash report ===");
					writer.WriteLine("pid: " + newestPid);
					foreach (var file in groups[newestPid].OrderBy(f => f, StringComparer.Ordinal))
					{
						writer.WriteLine("--- " + Path.GetFileName(file) + " ---");
						try
						{
							using (var reader = new StreamReader(file))
							{
								var buffer = new char[16384];
								int n;
								while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
									writer.Write(buffer, 0, n);
							}
							writer.WriteLine();
						}
						catch (Exception e)
						{
							writer.WriteLine("[unreadable: " + e.GetType().Name + "]");
						}
					}
				}
				archived = true;
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Could not write " + prevPath + ": " + e.GetType().Name);
			}

			if (archived && YaOptGlobal.Settings.EnhancedCrashLog.Enabled)
				LogPreview(prevPath);

			// Retention: only the archive and this session's report (recreated by the native
			// install) survive the launch.
			foreach (var pair in groups)
			{
				if (!archived && pair.Key == newestPid)
					continue;
				foreach (var file in pair.Value)
					TryDeleteFile(file);
			}
		}

		private static void LogPreview(string prevPath)
		{
			try
			{
				var preview = new StringBuilder();
				using (var reader = new StreamReader(prevPath))
				{
					var buffer = new char[PreviewChars];
					var n = reader.Read(buffer, 0, buffer.Length);
					if (n > 0)
						preview.Append(buffer, 0, n);
					if (!reader.EndOfStream)
						preview.Append("\n... (preview truncated; full copy: ").Append(prevPath).Append(')');
				}
				YaOptMod.Log("[Crash log] Previous run's report:\n" + preview);
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Could not read " + prevPath + ": " + e.GetType().Name);
			}
		}

		private static void TryDeleteFile(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception)
			{
				// Locked by antivirus or a stale handle: it is pruned on the next launch.
			}
		}

		private static bool TryGetPid(string fileName, out string pid)
		{
			pid = null;
			if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal))
				return false;
			var rest = fileName.Substring(FilePrefix.Length);
			var dot = rest.IndexOf('.');
			if (dot <= 0)
				return false;
			pid = rest.Substring(0, dot);
			return int.TryParse(pid, out _);
		}

		private static void Append(string path, string text)
		{
			try
			{
				// FileMode.Append also grants FILE_WRITE_DATA, which lets a writer's stale file
				// pointer overwrite text the native handler appended in the meantime. Asking for
				// FILE_APPEND_DATA alone makes Windows force every write to the current end.
				var raw = CreateFileForAppend(path, FileAppendData, FileShareAll, IntPtr.Zero,
					OpenAlways, FileAttributeNormal, IntPtr.Zero);
				if (raw == IntPtr.Zero || raw == new IntPtr(-1))
					return;

				using (var handle = new SafeFileHandle(raw, true))
				using (var stream = new FileStream(handle, FileAccess.Write))
				using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
				{
					writer.Write(text);
				}
			}
			catch (Exception)
			{
				// Never let a diagnostic writer break the exception handler that called it.
			}
		}

		/// <summary>
		/// Redirects fd 2 to the shared crash log, unbuffered, so text the runtime prints on its way
		/// out (for example the repeating frames of a stack overflow) survives an instant exit.
		/// </summary>
		private static void InstallStderrCapture()
		{
			// Two mods cannot both own fd 2. gpp redirects it too, and its own probe refuses to
			// attach when stderr is already a file, so yielding here avoids silently disabling it.
			if (YaOptGlobal.HasMod(GppPackageId))
			{
				YaOptMod.Log("[Crash log] Fatal stderr capture skipped: " + GppPackageId +
							 " is loaded and owns the process stderr.");
				return;
			}

			var stream = __acrt_iob_func(2u);
			if (stream == IntPtr.Zero)
			{
				YaOptMod.Warning("[Crash log] Fatal stderr capture unavailable: no CRT stderr stream.");
				return;
			}

			try
			{
				var fd = _fileno(stream);
				if (fd >= 0)
				{
					var handle = _get_osfhandle(fd);
					var type = handle != IntPtr.Zero && handle != new IntPtr(-1)
						? GetFileType(handle)
						: 0u;
					if (type == FileTypeDisk || type == FileTypeChar)
					{
						YaOptMod.Log("[Crash log] Fatal stderr capture skipped: stderr is already a " +
									 (type == FileTypeDisk ? "file" : "console") + ".");
						return;
					}
				}

				if (_wfreopen(_sharedLogPath, "a", stream) == IntPtr.Zero)
				{
					YaOptMod.Warning("[Crash log] Fatal stderr capture failed for " + _sharedLogPath);
					return;
				}

				setvbuf(stream, IntPtr.Zero, Unbuffered, UIntPtr.Zero);
				YaOptMod.Log("[Crash log] Process stderr is being recorded to " + _sharedLogPath);
			}
			catch (Exception e)
			{
				YaOptMod.Warning("[Crash log] Fatal stderr capture failed: " + e.GetType().Name);
			}
		}

		[DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr __acrt_iob_func(uint index);

		[DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		private static extern IntPtr _wfreopen(string path, string mode, IntPtr stream);

		[DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int setvbuf(IntPtr stream, IntPtr buffer, int mode, UIntPtr size);

		[DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int _fileno(IntPtr stream);

		[DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr _get_osfhandle(int fd);

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CreateFileForAppend(string path, uint access, uint share,
			IntPtr security, uint creation, uint flags, IntPtr template);

		[DllImport("kernel32.dll")]
		private static extern uint GetFileType(IntPtr handle);
	}
}
