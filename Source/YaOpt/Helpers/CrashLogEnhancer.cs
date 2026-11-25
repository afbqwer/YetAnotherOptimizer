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
	/// </remarks>
	public static class CrashLogEnhancer
	{
		private const string FilePrefix = "YaOpt_Crash_";
		private const string LogSuffix = ".log";
		private const string PrevName = "YaOpt_Crash-prev.log";
		private const string GppPackageId = "wowgag.guiperformancepatch";
		private const int PreviewChars = 6000;
		private const int PrevFileChars = 20000;
		private const int Unbuffered = 4;
		private const uint FileTypeDisk = 1u;
		private const uint FileTypeChar = 2u;
		private const uint FileAppendData = 0x4u;
		private const uint FileShareAll = 0x7u;
		private const uint OpenAlways = 4u;
		private const uint FileAttributeNormal = 0x80u;

		private static string _sharedLogPath;

		public static void Init()
		{
			try
			{
				var dir = GenFilePaths.SaveDataFolderPath;
				_sharedLogPath = Path.Combine(dir, $"{FilePrefix}{Environment.ProcessId}{LogSuffix}");

				// The two settings are independent: disabling the managed stack writer must not
				// silently disable the stderr capture the user explicitly enabled.
				if (YaOptGlobal.Settings.EnhancedCrashLog.Enabled)
					SummarizePrevious(dir);

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
		public static void TryDeleteEmptyManagedLog()
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
		/// Copies the newest previous crash report to YaOpt_Crash-prev.log and prints a preview.
		/// Old reports are deliberately left in place: deleting other sessions' diagnostics during
		/// startup would be a surprising side effect, and a stack overflow can leave several files.
		/// </summary>
		private static void SummarizePrevious(string dir)
		{
			var currentPid = Environment.ProcessId.ToString();
			var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

			foreach (var file in Directory.GetFiles(dir, FilePrefix + "*.log"))
			{
				var name = Path.GetFileName(file);
				if (!TryGetPid(name, out var pid) || pid == currentPid)
					continue;
				var info = new FileInfo(file);
				if (info.Length == 0)
					continue;
				if (!groups.TryGetValue(pid, out var list))
					groups[pid] = list = new List<string>();
				list.Add(file);
			}

			if (groups.Count == 0)
				return;

			var newestPid = groups.Keys
				.OrderByDescending(pid => groups[pid].Max(file => new FileInfo(file).LastWriteTime))
				.First();

			var sb = new StringBuilder()
				.AppendLine("=== YaOpt previous crash report ===")
				.AppendLine("pid: " + newestPid);
			foreach (var file in groups[newestPid].OrderBy(file => file, StringComparer.Ordinal))
			{
				sb.AppendLine("--- " + Path.GetFileName(file) + " ---");
				try
				{
					var text = File.ReadAllText(file);
					sb.Append(text.Length > PrevFileChars
						? text.Substring(0, PrevFileChars) + "\n... (truncated; see original file)"
						: text);
					sb.AppendLine();
				}
				catch (Exception e)
				{
					sb.AppendLine("[unreadable: " + e.GetType().Name + "]");
				}
			}

			var prevPath = Path.Combine(dir, PrevName);
			try
			{
				File.WriteAllText(prevPath, sb.ToString(), Encoding.UTF8);
			}
			catch (Exception e)
			{
				YaOptMod.Warning("Could not write " + prevPath + ": " + e.GetType().Name);
			}

			var report = sb.ToString();
			var preview = report.Length > PreviewChars
				? report.Substring(0, PreviewChars) +
				  $"\n... ({report.Length - PreviewChars:N0} more chars; full copy: {prevPath})"
				: report;
			YaOptMod.Log("[Crash log] Previous run's report:\n" + preview);

			// Mark this group as consumed so the next launch does not print the same report
			// again. Renaming keeps the evidence without deleting another session's diagnostics.
			foreach (var file in groups[newestPid])
			{
				try
				{
					var seen = file + ".seen";
					if (File.Exists(seen))
						seen = file + ".seen." + DateTime.UtcNow.Ticks;
					File.Move(file, seen);
				}
				catch (Exception)
				{
					// A file still held by another process will simply be reported again next time.
				}
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
