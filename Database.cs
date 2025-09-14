using System;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;

namespace LogFileCollector
{
    /// <summary>
    /// SQLite persistence wrapper. 
    /// Schema:
    ///   CREATE TABLE IF NOT EXISTS Copied(
    ///     FullPath TEXT NOT NULL,
    ///     LastWriteTimeUtc INTEGER NOT NULL, -- ticks
    ///     Length INTEGER NOT NULL,
    ///     PRIMARY KEY(FullPath, LastWriteTimeUtc, Length)
    ///   );
    /// </summary>
    public class Database
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public Database(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = "Data Source=" + _dbPath + ";Cache=Shared";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath) ?? ".");
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE IF NOT EXISTS Copied(" +
                        " FullPath TEXT NOT NULL," +
                        " LastWriteTimeUtc INTEGER NOT NULL," +
                        " Length INTEGER NOT NULL," +
                        " PRIMARY KEY(FullPath, LastWriteTimeUtc, Length)" +
                        ");";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Returns true if a file with the same identity has already been copied.
        /// Identity = (FullPath, LastWriteTimeUtc, Length)
        /// </summary>
        public bool IsFileAlreadyCopied(string fullPath, DateTime lastWriteUtc, long length)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM Copied WHERE FullPath=$p AND LastWriteTimeUtc=$t AND Length=$l LIMIT 1;";
                    cmd.Parameters.AddWithValue("$p", fullPath);
                    cmd.Parameters.AddWithValue("$t", lastWriteUtc.Ticks);
                    cmd.Parameters.AddWithValue("$l", length);
                    object o = cmd.ExecuteScalar();
                    return o != null;
                }
            }
        }

        /// <summary>
        /// Inserts a record for the provided file identity.
        /// </summary>
        public void MarkFileCopied(string fullPath, DateTime lastWriteUtc, long length)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR IGNORE INTO Copied(FullPath, LastWriteTimeUtc, Length) VALUES($p,$t,$l);";
                    cmd.Parameters.AddWithValue("$p", fullPath);
                    cmd.Parameters.AddWithValue("$t", lastWriteUtc.Ticks);
                    cmd.Parameters.AddWithValue("$l", length);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Generates a unique path in the target directory when a name collision happens.
        /// Strategies:
        ///  - counter   => file_1.ext, file_2.ext
        ///  - timestamp => file_yyyyMMdd_HHmmssfff.ext
        ///  - guid      => file_XXXXXXXX.ext
        /// </summary>
        public string GetUniqueTargetPath(string targetFolder, string fileName, string renameStrategy)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string candidate = Path.Combine(targetFolder, fileName);

            if (!File.Exists(candidate)) return candidate;

            string strategy = (renameStrategy ?? "counter").Trim().ToLowerInvariant();
            if (strategy == "timestamp")
            {
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
                return Path.Combine(targetFolder, name + "_" + ts + ext);
            }
            else if (strategy == "guid")
            {
                string g = Guid.NewGuid().ToString("N").Substring(0, 8);
                return Path.Combine(targetFolder, name + "_" + g + ext);
            }
            else
            {
                // counter (default)
                int i = 1;
                while (true)
                {
                    candidate = Path.Combine(targetFolder, string.Format("{0}_{1}{2}", name, i, ext));
                    if (!File.Exists(candidate)) return candidate;
                    i++;
                    if (i > 1000000) throw new IOException("Could not find a unique file name.");
                }
            }
        }
    }
}
