namespace LogFileCollector
{
    /// <summary>
    /// Strongly typed mapping of appsettings.json.
    /// </summary>
    public class Appsettings
    {
        public string SourceFolder { get; set; } = "";
        public string TargetFolder { get; set; } = "";
        public string Filter { get; set; } = "*.*";
        public bool IncludeSubdirectories { get; set; } = true;
        public int FileCreatedDelayMs { get; set; } = 500;
        public string DatabasePath { get; set; } = "copied.db";
        public string RenameStrategy { get; set; } = "counter";
        public int PeriodicRescanMinutes { get; set; }
        public LoggingSettings Logging { get; set; } = new LoggingSettings();
    }

    /// <summary>
    /// Logging section from config.
    /// Verbose = force per-file skip lines at Information level (even if LogLevel > Debug).
    /// </summary>
    public class LoggingSettings
    {
        public string LogFilePath { get; set; } = "log.txt";
        public string LogOutputTemplate { get; set; } =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        public string LogLevel { get; set; } = "Information";   // Debug|Information|Warning|Error
        public string RollingInterval { get; set; } = "Day";     // Day|Month|Year|Infinite
        public int RetainedFileCountLimit { get; set; } = 10;
        public bool Verbose { get; set; } = false;
    }
}
