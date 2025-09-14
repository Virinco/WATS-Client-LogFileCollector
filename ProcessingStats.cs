namespace LogFileCollector
{
    /// <summary>
    /// Tracks statistics during file processing.
    /// </summary>
    public class ProcessingStats
    {
        public int Scanned { get; set; }
        public int Copied { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }

        public override string ToString()
            => $"Scanned={Scanned}, Copied={Copied}, Skipped={Skipped}, Errors={Errors}";
    }
}
