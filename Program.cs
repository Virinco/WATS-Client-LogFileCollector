using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace LogFileCollector
{
    /// <summary>
    /// Application entry point. 
    /// - Loads configuration from %ProgramData%\Virinco\WATS\LogFileCollector\appsettings.json
    /// - Configures Serilog (console + rolling file)
    /// - Handles --reset and --rescan
    /// - Starts the watcher and optional periodic full rescans
    /// </summary>
    internal class Program
    {
        private static Timer _periodicRescanTimer;

        static int Main(string[] args)
        {
            // Resolve ProgramData config dir (works for both service and user contexts)
            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Virinco", "WATS", "LogFileCollector");
            Directory.CreateDirectory(dataDir);

            // Load config from ProgramData
            string configPath = Path.Combine(dataDir, "appsettings.json");
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("Config not found: " + configPath);
                return 2;
            }

            try
            {
                AppSettings config = ConfigLoader.Load(configPath);

                // Resolve DB + Log paths (relative => under ProgramData; absolute/UNC => as-is)
                Func<string, string> resolveInData = p => Path.IsPathRooted(p) ? p : Path.Combine(dataDir, p);
                string dbPath = resolveInData(config.DatabasePath);
                string logPath = resolveInData(config.Logging.LogFilePath);

                // Map LogLevel string to Serilog LogEventLevel (use simple switch for C# 7.3 compatibility)
                LogEventLevel minLevel;
                switch ((config.Logging.LogLevel ?? "Information").Trim().ToLowerInvariant())
                {
                    case "debug": minLevel = LogEventLevel.Debug; break;
                    case "warning": minLevel = LogEventLevel.Warning; break;
                    case "error": minLevel = LogEventLevel.Error; break;
                    case "verbose": minLevel = LogEventLevel.Verbose; break;
                    default: minLevel = LogEventLevel.Information; break;
                }

                // Map RollingInterval
                RollingInterval interval;
                switch ((config.Logging.RollingInterval ?? "Day").Trim().ToLowerInvariant())
                {
                    case "hour": interval = RollingInterval.Hour; break;
                    case "month": interval = RollingInterval.Month; break;
                    case "year": interval = RollingInterval.Year; break;
                    case "infinite": interval = RollingInterval.Infinite; break;
                    default: interval = RollingInterval.Day; break;
                }

                // Configure Serilog
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Is(minLevel)
                    .WriteTo.Console(outputTemplate: config.Logging.LogOutputTemplate)
                    .WriteTo.File(
                        path: logPath,
                        outputTemplate: config.Logging.LogOutputTemplate,
                        rollingInterval: interval,
                        retainedFileCountLimit: config.Logging.RetainedFileCountLimit)
                    .CreateLogger();

                Log.Information("LogFileCollector starting.");

                bool reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
                bool rescan = args.Any(a => a.Equals("--rescan", StringComparison.OrdinalIgnoreCase));

                // Reset option: delete DB file
                if (reset && File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    Log.Warning("Database reset: {Db}", dbPath);
                }

                // Initialize DB and processor
                Database db = new Database(dbPath);
                FileProcessor processor = new FileProcessor(config, db);

                // One-time rescan on startup if requested
                if (rescan)
                {
                    Log.Information("Rescan starting (Filter={Filter}, Subdirs={Subdirs})",
                        config.Filter, config.IncludeSubdirectories);
                    processor.ProcessAllFiles();
                    Log.Information("Rescan completed. Run={RunStats} Total={TotalStats}", processor.RunStats, processor.TotalStats);
                    Log.Information("Switching to watcher mode…");
                }

                // Always start watcher
                processor.StartWatching();

                // Periodic rescan (safety net against missed events)
                if (config.PeriodicRescanMinutes > 0)
                {
                    TimeSpan rescanInterval = TimeSpan.FromMinutes(config.PeriodicRescanMinutes);
                    _periodicRescanTimer = new Timer(state =>
                    {
                        try
                        {
                            Log.Information("Starting periodic rescan (every {Minutes} minutes)...", config.PeriodicRescanMinutes);
                            processor.ProcessAllFiles();
                            Log.Information("Periodic rescan completed. Run={RunStats} Total={TotalStats}", processor.RunStats, processor.TotalStats);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error during periodic rescan");
                        }
                    }, null, rescanInterval, rescanInterval);

                    Log.Information("Periodic rescan enabled (every {Minutes} minutes)", config.PeriodicRescanMinutes);
                }

                // Keep running until Ctrl+C
                Log.Information("Watcher running. Press Ctrl+C to stop.");
                ManualResetEvent exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
                exitEvent.WaitOne();

                // Final stats on shutdown
                Log.Information("Shutdown. Final statistics: Total={TotalStats}", processor.TotalStats);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Fatal error");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
