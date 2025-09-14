using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;

namespace LogFileCollector
{
    internal class Program
    {
        static int Main(string[] args)
        {
            // Resolve ProgramData config dir
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
                var config = ConfigLoader.Load(configPath);

                // Resolve DB + Log paths (relative => under ProgramData; absolute/UNC => as-is)
                string ResolveInData(string p) => Path.IsPathRooted(p) ? p : Path.Combine(dataDir, p);
                string dbPath = ResolveInData(config.DatabasePath);
                string logPath = ResolveInData(config.Logging.LogFilePath);

                // Map LogLevel string to Serilog LogEventLevel
                var minLevel = (config.Logging.LogLevel ?? "Information").Trim().ToLowerInvariant() switch
                {
                    "debug" => LogEventLevel.Debug,
                    "warning" => LogEventLevel.Warning,
                    "error" => LogEventLevel.Error,
                    "verbose" => LogEventLevel.Verbose,
                    _ => LogEventLevel.Information
                };

                // Map RollingInterval string to Serilog RollingInterval
                var interval = (config.Logging.RollingInterval ?? "Day").Trim().ToLowerInvariant() switch
                {
                    "hour" => RollingInterval.Hour,
                    "month" => RollingInterval.Month,
                    "year" => RollingInterval.Year,
                    "infinite" => RollingInterval.Infinite,
                    _ => RollingInterval.Day
                };

                // Configure Serilog logging
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Is(minLevel)
                    .WriteTo.Console(outputTemplate: config.Logging.LogOutputTemplate)
                    .WriteTo.File(
                        path: logPath,
                        outputTemplate: config.Logging.LogOutputTemplate,
                        rollingInterval: interval,
                        retainedFileCountLimit: config.Logging.RetainedFileCountLimit)
                    .CreateLogger();

                Log.Information("LogFileCollector starting…");

                // Command-line options
                bool reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
                bool rescan = args.Any(a => a.Equals("--rescan", StringComparison.OrdinalIgnoreCase));

                // Reset option: delete DB file
                if (reset && File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    Log.Warning("Database reset: {Db}", dbPath);
                }

                // Initialize DB and processor
                var db = new Database(dbPath);
                var processor = new FileProcessor(config, db);

                // One-time rescan on startup if requested
                if (rescan)
                {
                    Log.Information("Rescan starting (Filter={Filter}, Subdirs={Subdirs})",
                        config.Filter, config.IncludeSubdirectories);
                    processor.ProcessAllFiles();
                    Log.Information("Rescan completed. {Stats}", processor.Stats);
                    Log.Information("Switching to watcher mode…");
                }

                // Always start watcher (unless config was fatally missing earlier)
                processor.StartWatching();

                // Periodic rescan (safety net against missed events)
                if (config.PeriodicRescanMinutes > 0)
                {
                    var rescanInterval = TimeSpan.FromMinutes(config.PeriodicRescanMinutes);
                    var timer = new System.Threading.Timer(_ =>
                    {
                        try
                        {
                            Log.Information("Starting periodic rescan (every {Minutes} minutes)...", config.PeriodicRescanMinutes);
                            processor.ProcessAllFiles();
                            Log.Information("Periodic rescan completed. {Stats}", processor.Stats);
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
                var exitEvent = new System.Threading.ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
                exitEvent.WaitOne();

                // Final stats on shutdown
                Log.Information("Shutdown. Final statistics: {Stats}", processor.Stats);
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
