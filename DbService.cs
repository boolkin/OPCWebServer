using System;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OPCWebServer
{
    public class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly int _retentionDays;
        private readonly ConcurrentQueue<(int tagIndex, object value, long timestamp)> _writeQueue;
        private Timer _cleanupTimer;
        private bool _disposed = false;
        private readonly object _lockObj = new object();

        public DbService(string dbPath, int retentionDays)
        {
            _dbPath = dbPath;
            _retentionDays = retentionDays;
            _writeQueue = new ConcurrentQueue<(int, object, long)>();

            InitializeDatabase();
            StartCleanupTimer();
        }

        private void InitializeDatabase()
        {
            try
            {
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var connectionString = $"Data Source={_dbPath};Version=3;";
                
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    
                    // Enable WAL mode for better concurrency
                    using (var cmd = new SQLiteCommand("PRAGMA journal_mode=WAL;", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    // Create table
                    string createTableSql = @"
                        CREATE TABLE IF NOT EXISTS raw_data (
                            timestamp INTEGER NOT NULL,
                            tagIndex INTEGER NOT NULL,
                            value TEXT,
                            PRIMARY KEY (timestamp, tagIndex)
                        );";
                    
                    using (var cmd = new SQLiteCommand(createTableSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    // Create index for faster queries by tagIndex
                    string createIndexSql = @"
                        CREATE INDEX IF NOT EXISTS idx_tagIndex ON raw_data(tagIndex);";
                    
                    using (var cmd = new SQLiteCommand(createIndexSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"DB init error: {ex.Message}");
            }
        }

        private void StartCleanupTimer()
        {
            // Run cleanup once per day
            _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.Zero, TimeSpan.FromHours(24));
        }

        private void CleanupOldData(object state)
        {
            try
            {
                var cutoffTime = DateTimeOffset.Now.AddDays(-_retentionDays).ToUnixTimeSeconds();
                var connectionString = $"Data Source={_dbPath};Version=3;";
                
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string deleteSql = "DELETE FROM raw_data WHERE timestamp < @cutoff;";
                    
                    using (var cmd = new SQLiteCommand(deleteSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@cutoff", cutoffTime);
                        int rowsDeleted = cmd.ExecuteNonQuery();
                        if (rowsDeleted > 0)
                        {
                            LogMessage($"DB cleanup: deleted {rowsDeleted} old records");
                        }
                    }
                    
                    // Vacuum to reclaim space
                    using (var cmd = new SQLiteCommand("VACUUM;", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"DB cleanup error: {ex.Message}");
            }
        }

        public void EnqueueRecord(int tagIndex, object value)
        {
            if (_disposed) return;
            
            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            _writeQueue.Enqueue((tagIndex, value, timestamp));
            
            // Flush queue if it has items
            FlushQueue();
        }

        private void FlushQueue()
        {
            // Try to flush immediately but don't block
            Task.Run(() =>
            {
                lock (_lockObj)
                {
                    if (_writeQueue.IsEmpty) return;

                    try
                    {
                        var connectionString = $"Data Source={_dbPath};Version=3;";
                        
                        using (var conn = new SQLiteConnection(connectionString))
                        {
                            conn.Open();
                            
                            using (var transaction = conn.BeginTransaction())
                            {
                                string insertSql = "INSERT OR REPLACE INTO raw_data (timestamp, tagIndex, value) VALUES (@ts, @idx, @val);";
                                
                                using (var cmd = new SQLiteCommand(insertSql, conn, transaction))
                                {
                                    cmd.Prepare();
                                    
                                    while (_writeQueue.TryDequeue(out var record))
                                    {
                                        cmd.Parameters.Clear();
                                        cmd.Parameters.AddWithValue("@ts", record.timestamp);
                                        cmd.Parameters.AddWithValue("@idx", record.tagIndex);
                                        cmd.Parameters.AddWithValue("@val", record.value?.ToString() ?? "");
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                
                                transaction.Commit();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"DB write error: {ex.Message}");
                    }
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _cleanupTimer?.Dispose();
            
            // Flush remaining items
            FlushQueue();
            Thread.Sleep(100); // Give time to flush
            
            _disposed = true;
        }

        public event Action<string> LogMessage;
    }
}
