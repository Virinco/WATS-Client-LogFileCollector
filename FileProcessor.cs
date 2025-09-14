using System;
using System.IO;
using System.Threading;
using Serilog;

namespace LogFileCollector
{
    /// <summary>
    /// Scans and copies files; sets up a watcher for real-time events.
    /// Honors IncludeSubdirectories and FileCreatedDelayMs.
    /// Provides per-run statistics and richer network error logging.
    /// </summary>
    public class FileProcessor
    {
        private readonly Appsettings _config;
        private readonly Database _db;
        public ProcessingStats Stats { get; } = new ProcessingStats();

        public FileProcessor(Appsettings config, Database db)
        {
            _config = config;
            _db = db;
        }

        /// <summary>Rescan entire tree and copy all files not yet copied.</summary>
        public int ProcessAllFiles()
        {
            try
            {
                if (!Directory.Exists(_config.SourceFolder))
                {
                    Log.Error("Source folder not found: {Path}", _config.SourceFolder);
                    return 0;
                }

                var option = _config.IncludeSubdirectories ?
                             SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (var file in Directory.EnumerateFiles(_config.SourceFolder, _config.Filter, option))
                {
                    Stats.Scanned++;
                    if (TryCopy(file)) Stats.Copied++; else Stats.Skipped++;
                }
            }
            catch (UnauthorizedAccessException ua)
            {
                Stats.Errors++;
                if (IsNetworkPath(_config.SourceFolder))
                    Log.Error(ua, "Access denied to network source folder: {Path}. Check share perms or the task account.", _config.SourceFolder);
                else
                    Log.Error(ua, "Access denied reading source folder: {Path}", _config.SourceFolder);
            }
            catch (IOException ioEx)
            {
                Stats.Errors++;
                if (IsNetworkPath(_config.SourceFolder))
                    Log.Error(ioEx, "Network share issue reading {Path} (unavailable, offline, or path changed).", _config.SourceFolder);
                else
                    Log.Error(ioEx, "I/O error while scanning source folder {Path}", _config.SourceFolder);
            }
            catch (Exception ex)
            {
                Stats.Errors++;
                Log.Error(ex, "Unexpected error scanning source folder {Path}", _config.SourceFolder);
            }

            Log.Information("Processing summary: {Stats}", Stats);
            return Stats.Copied;
        }

        /// <summary>Start FileSystemWatcher, honoring IncludeSubdirectories and FileCreatedDelayMs.</summary>
        public void StartWatching()
        {
            if (!Directory.Exists(_config.SourceFolder))
            {
                Log.Error("Source folder not found: {Path}", _config.SourceFolder);
                return;
            }

            var watcher = new FileSystemWatcher(_config.SourceFolder, _config.Filter)
            {
                IncludeSubdirectories = _config.IncludeSubdirectories,
                EnableRaisingEvents = true
            };

            FileSystemEventHandler handler = (s, e) =>
            {
                try
                {
                    // Let the writer finish; protects against sharing violations/partial writes
                    if (_config.FileCreatedDelayMs > 0)
                        Thread.Sleep(_config.FileCreatedDelayMs);

                    Stats.Scanned++;
                    if (TryCopy(e.FullPath)) Stats.Copied++; else Stats.Skipped++;
                }
                catch (Exception ex)
                {
                    Stats.Errors++;
                    Log.Error(ex, "Watcher error for {File}", e.FullPath);
                }
            };

            watcher.Created += handler;
            watcher.Changed += handler;

            Log.Information("Watching {Path} (Filter={Filter}, Subdirs={Subdirs}, DelayMs={Delay})",
                _config.SourceFolder, _config.Filter, _config.IncludeSubdirectories, _config.FileCreatedDelayMs);
        }

        /// <summary>Try copy one file if not already tracked; applies rename strategy.</summary>
        private bool TryCopy(string sourceFile)
        {
            try
            {
                var fi = new FileInfo(sourceFile);
                if (!fi.Exists)
                {
                    LogAtSkipLevel("Skipped (not found): {File}", sourceFile);
                    return false;
                }

                if (_db.IsCopied(fi.FullName, fi.LastWriteTimeUtc, fi.Length))
                {
                    LogAtSkipLevel("Skipped (already copied): {File}", fi.FullName);
                    return false;
                }

                Directory.CreateDirectory(_config.TargetFolder);

                string targetPath = Path.Combine(_config.TargetFolder, fi.Name);
                targetPath = EnsureUniqueTarget(targetPath);

                File.Copy(fi.FullName, targetPath);
                _db.MarkCopied(fi.FullName, fi.LastWriteTimeUtc, fi.Length);

                Log.Information("Copied {Source} -> {Target}", fi.FullName, targetPath);
                return true;
            }
            catch (UnauthorizedAccessException ua)
            {
                Stats.Errors++;
                if (IsNetworkPath(_config.SourceFolder))
                    Log.Error(ua, "Network permission issue copying {File} from {Src}", sourceFile, _config.SourceFolder);
                else
                    Log.Error(ua, "Permission issue copying {File}", sourceFile);
                return false;
            }
            catch (IOException ioEx)
            {
                Stats.Errors++;
                if (IsNetworkPath(_config.SourceFolder))
                    Log.Error(ioEx, "Network I/O error copying {File} (share offline, transient lock, or path changed).", sourceFile);
                else
                    Log.Error(ioEx, "I/O error copying {File}", sourceFile);
                return false;
            }
            catch (Exception ex)
            {
                Stats.Errors++;
                Log.Error(ex, "Unexpected error copying {File}", sourceFile);
                return false;
            }
        }

        /// <summary>Apply rename strategy until target path is unique.</summary>
        private string EnsureUniqueTarget(string initialPath)
        {
            string candidate = initialPath;
            string dir = Path.GetDirectoryName(initialPath)!;
            string baseName = Path.GetFileNameWithoutExtension(initialPath);
            string ext = Path.GetExtension(initialPath);
            int counter = 1;

            while (File.Exists(candidate))
            {
                switch ((_config.RenameStrategy ?? "counter").ToLowerInvariant())
                {
                    case "timestamp":
                        candidate = Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                        break;
                    case "guid":
                        candidate = Path.Combine(dir, $"{baseName}_{Guid.NewGuid():N}{ext}");
                        break;
                    default: // counter
                        candidate = Path.Combine(dir, $"{baseName}_{counter++}{ext}");
                        break;
                }
            }
            return candidate;
        }

        private static bool IsNetworkPath(string path) => path.StartsWith(@"\\");
        private void LogAtSkipLevel(string messageTemplate, string value)
        {
            // If Verbose enabled, bubble skip lines to Information; otherwise keep them at Debug
            if (_config.Logging.Verbose) Log.Information(messageTemplate, value);
            else Log.Debug(messageTemplate, value);
        }
    }
}
