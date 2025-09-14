using System;
using System.IO;
using System.Threading;
using Serilog;

namespace LogFileCollector
{
    /// <summary>
    /// Simple stats container. We keep one per run (RunStats) and one cumulative (TotalStats).
    /// </summary>
    public class ProcessingStats
    {
        public int Scanned { get; set; }
        public int Copied { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }

        public override string ToString()
        {
            return string.Format("Scanned={0}, Copied={1}, Skipped={2}, Errors={3}", Scanned, Copied, Skipped, Errors);
        }

        public void Reset()
        {
            Scanned = 0;
            Copied = 0;
            Skipped = 0;
            Errors = 0;
        }

        public void Add(ProcessingStats other)
        {
            Scanned += other.Scanned;
            Copied += other.Copied;
            Skipped += other.Skipped;
            Errors += other.Errors;
        }
    }

    /// <summary>
    /// Encapsulates watcher + scanning logic.
    /// - Uses Created + Renamed events (Changed is noisy and can duplicate)
    /// - Increases InternalBufferSize to 64 KB for burst resistance
    /// - Respects FileCreatedDelayMs before copying to avoid partial files
    /// - Uses Database for duplicate prevention based on (path, lastWriteUtc, length)
    /// </summary>
    public class FileProcessor
    {
        private readonly AppSettings _config;
        private readonly Database _db;
        private FileSystemWatcher _watcher;

        public ProcessingStats RunStats { get; private set; } = new ProcessingStats();
        public ProcessingStats TotalStats { get; private set; } = new ProcessingStats();

        public FileProcessor(AppSettings config, Database db)
        {
            _config = config;
            _db = db;
        }

        /// <summary>
        /// Full pass over the source folder. Resets RunStats each time.
        /// </summary>
        public void ProcessAllFiles()
        {
            RunStats.Reset();

            SearchOption opt = _config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Enumerate defensively; if SourceFolder is a network/UNC path, provide more actionable errors.
            string[] files;
            try
            {
                files = Directory.GetFiles(_config.SourceFolder, _config.Filter, opt);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enumerate files in SourceFolder={Folder}. If this is a network or cloud path, verify UNC accessibility and permissions (read only is sufficient).", _config.SourceFolder);
                return;
            }

            foreach (string fullPath in files)
            {
                HandleOne(fullPath);
            }

            // After a run, fold into totals and log
            TotalStats.Add(RunStats);
            Log.Information("Processing summary: {RunStats}", RunStats);
            Log.Information("Total so far: {TotalStats}", TotalStats);
        }

        /// <summary>
        /// Start the FileSystemWatcher for incremental changes.
        /// </summary>
        public void StartWatching()
        {
            _watcher = new FileSystemWatcher(_config.SourceFolder, _config.Filter);
            _watcher.IncludeSubdirectories = _config.IncludeSubdirectories;
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
            _watcher.InternalBufferSize = 64 * 1024; // Max 64 KB

            _watcher.Created += OnCreatedOrRenamed;
            _watcher.Renamed += OnCreatedOrRenamed;

            _watcher.EnableRaisingEvents = true;

            Log.Information("Watching {Source} (Filter={Filter}, Subdirs={Subdirs}, DelayMs={Delay})",
                _config.SourceFolder, _config.Filter, _config.IncludeSubdirectories, _config.FileCreatedDelayMs);
        }

        private void OnCreatedOrRenamed(object sender, FileSystemEventArgs e)
        {
            HandleOne(e.FullPath, fromWatcher: true);
        }

        /// <summary>
        /// Process a single file path:
        /// - wait FileCreatedDelayMs (to avoid partial writes)
        /// - check DB for duplication
        /// - copy to target (with rename strategy on collision)
        /// - record in DB
        /// Stats:
        /// - For watcher events, we reset RunStats to report a per-event summary (keeps logs informative)
        /// - For full scans, ProcessAllFiles() resets before the loop and aggregates.
        /// </summary>
        private void HandleOne(string fullPath, bool fromWatcher = false)
        {
            try
            {
                if (fromWatcher)
                {
                    // For single-event summaries in logs
                    RunStats.Reset();
                }

                // Wait out writer processes (cheap insurance)
                Thread.Sleep(_config.FileCreatedDelayMs);

                FileInfo fi = new FileInfo(fullPath);
                if (!fi.Exists) return; // vanished or temp

                RunStats.Scanned++;

                // Duplicate check against DB
                if (_db.IsFileAlreadyCopied(fullPath, fi.LastWriteTimeUtc, fi.Length))
                {
                    Log.Debug("Skipped (already copied): {File}", fullPath);
                    RunStats.Skipped++;
                    if (fromWatcher)
                    {
                        TotalStats.Add(RunStats);
                        Log.Information("Watcher event summary: {RunStats}", RunStats);
                        Log.Information("Total so far: {TotalStats}", TotalStats);
                    }
                    return;
                }

                // Ensure target folder exists
                if (!Directory.Exists(_config.TargetFolder))
                {
                    Directory.CreateDirectory(_config.TargetFolder);
                }

                // Resolve unique target path per strategy
                string targetPath = _db.GetUniqueTargetPath(_config.TargetFolder, fi.Name, _config.RenameStrategy);

                // Copy (no overwrite)
                File.Copy(fullPath, targetPath, false);

                // Record in DB
                _db.MarkFileCopied(fullPath, fi.LastWriteTimeUtc, fi.Length);

                RunStats.Copied++;
                Log.Information("Copied {Source} -> {Target}", fullPath, targetPath);

                if (fromWatcher)
                {
                    TotalStats.Add(RunStats);
                    Log.Information("Watcher event summary: {RunStats}", RunStats);
                    Log.Information("Total so far: {TotalStats}", TotalStats);
                }
            }
            catch (Exception ex)
            {
                RunStats.Errors++;
                TotalStats.Errors++;
                Log.Error(ex, "Error processing file {File}", fullPath);
            }
        }
    }
}
