using System;
using Microsoft.Data.Sqlite;

namespace LogFileCollector
{
    /// <summary>
    /// SQLite database for tracking copied files to avoid duplicates.
    /// Uniqueness key: (FullPath, LastWriteTimeUtc, Length)
    /// </summary>
    public class Database
    {
        private readonly string _dbPath;

        public Database(string dbPath)
        {
            _dbPath = dbPath;
            Initialize();
        }

        private void Initialize()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CopiedFiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullPath TEXT NOT NULL,
                    LastWriteTimeUtc TEXT NOT NULL,
                    Length INTEGER NOT NULL,
                    UNIQUE(FullPath, LastWriteTimeUtc, Length)
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public bool IsCopied(string path, DateTime lastWriteTimeUtc, long length)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM CopiedFiles WHERE FullPath=$p AND LastWriteTimeUtc=$t AND Length=$l";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$t", lastWriteTimeUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$l", length);

            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }

        public void MarkCopied(string path, DateTime lastWriteTimeUtc, long length)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT OR IGNORE INTO CopiedFiles (FullPath, LastWriteTimeUtc, Length) VALUES ($p,$t,$l)";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$t", lastWriteTimeUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$l", length);

            cmd.ExecuteNonQuery();
        }

        public void Reset()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM CopiedFiles";
            cmd.ExecuteNonQuery();
        }
    }
}
