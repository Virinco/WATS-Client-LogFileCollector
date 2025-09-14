using System;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Events;

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

                // Map LogLevel
                var minLevel = (config.Logging.LogLevel ?? "Information").Trim().ToLowerInvariant() switch
                {
                    "debug" => LogEventLevel.Debug,
                    "warning" => LogEventLevel.Warning,
                    "error" => LogEventLevel.Error,
                    "verbose" => LogEventLevel.Verbose,
                    _ => LogEventLevel.Information
                };

                // Map RollingInterval
                var interval = (config.Logging.RollingInterval ?? "Day").Trim().ToLowerInvariant() switch
                {
                    "hour" => RollingInterval.Hour,
                    "month" => RollingInterval.Month,
                    "year" => RollingInterval.Year,
                    "infinite" => RollingInterval.Infinite,
                    _ => RollingInterval.Day
                };

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

                Log.Information("LogFileCollector starting…");

                bool reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
                bool rescan = args.Any(a => a.Equals("--rescan", StringComparison.OrdinalIgnoreCase));

                if (reset && File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    Log.Warning("Database reset: {Db}", dbPath);
                    // Continue with fresh DB
                }

                var db = new Database(dbPath);
                var processor = new FileProcessor(config, db);

                if (rescan)
                {
                    Log.Information("Rescan starting (Filter={Filter}, Subdirs={Subdirs})",
                        config.Filter, config.IncludeSubdirectories);
                    processor.ProcessAllFiles();
                    Log.Information("Rescan completed. {Stats}", processor.Stats);
                    Log.Information("Switching to watcher mode…");
                }

                // Always watch (unless we returned earlier on fatal config error)
                processor.StartWatching();

                Log.Information("Watcher running. Press Ctrl+C to stop.");
                var exitEvent = new System.Threading.ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
                exitEvent.WaitOne();

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
