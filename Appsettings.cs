using System;

namespace LogFileCollector
{
    /// <summary>
    /// Strongly typed configuration bound from appsettings.json.
    /// </summary>
    public class AppSettings
    {
        public string SourceFolder { get; set; }
        public string TargetFolder { get; set; }
        public string Filter { get; set; }
        public bool IncludeSubdirectories { get; set; }
        public int FileCreatedDelayMs { get; set; } // default 1000 ms (set in JSON)
        public string DatabasePath { get; set; }
        public string RenameStrategy { get; set; } // "counter" | "timestamp" | "guid"
        public int PeriodicRescanMinutes { get; set; } // 0 = disabled

        public LoggingSettings Logging { get; set; }
    }

    /// <summary>
    /// Logging configuration (Serilog)
    /// </summary>
    public class LoggingSettings
    {
        public string LogFilePath { get; set; }
        public string LogOutputTemplate { get; set; }
        public string LogLevel { get; set; }
        public string RollingInterval { get; set; }
        public int RetainedFileCountLimit { get; set; }
    }
}
