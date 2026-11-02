using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Diagnostics;

namespace ProWeighSuitePlatforms.Data
{
    public class TransactionStagingItem
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, Processing, Synced, Failed
        public int Attempts { get; set; }
        public DateTime? NextRetryUtc { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TransactionStagingRepository
    {
        private readonly string _dbPath;

        public TransactionStagingRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        private SqliteConnection GetConnection() => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeAsync()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS transaction_staging (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    transaction_id TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'Pending',
                    attempts INTEGER NOT NULL DEFAULT 0,
                    next_retry_utc TEXT,
                    last_error TEXT,
                    created_utc TEXT NOT NULL,
                    idempotency_key TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_transaction_staging_status ON transaction_staging(status);
                CREATE INDEX IF NOT EXISTS idx_transaction_staging_next_retry ON transaction_staging(next_retry_utc);
            ";

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> EnqueueAsync(string transactionId, string payloadJson, string? idempotencyKey = null)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO transaction_staging (transaction_id, payload_json, status, attempts, created_utc, idempotency_key)
                VALUES ($transactionId, $payload, 'Pending', 0, $createdUtc, $idempotencyKey);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$transactionId", transactionId);
            cmd.Parameters.AddWithValue("$payload", payloadJson ?? string.Empty);
            cmd.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$idempotencyKey", idempotencyKey ?? string.Empty);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<List<TransactionStagingItem>> GetPendingAsync(int limit = 50)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT id, transaction_id, payload_json, status, attempts, next_retry_utc, last_error, created_utc, idempotency_key
                FROM transaction_staging
                WHERE (status = 'Pending' OR status = 'Failed')
                  AND (next_retry_utc IS NULL OR next_retry_utc <= $now)
                ORDER BY created_utc
                LIMIT $limit
            ";
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$limit", limit);

            var list = new List<TransactionStagingItem>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new TransactionStagingItem
                {
                    Id = reader.GetInt32(0),
                    TransactionId = reader.GetString(1),
                    PayloadJson = reader.GetString(2),
                    Status = reader.GetString(3),
                    Attempts = reader.GetInt32(4),
                    NextRetryUtc = reader.IsDBNull(5) ? null : (DateTime?)DateTime.Parse(reader.GetString(5)),
                    LastError = reader.IsDBNull(6) ? null : reader.GetString(6),
                    CreatedUtc = DateTime.Parse(reader.GetString(7)),
                    IdempotencyKey = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
                list.Add(item);
            }

            return list;
        }

        public async Task MarkProcessingAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE transaction_staging SET status = 'Processing' WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarkSyncedAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM transaction_staging WHERE id = $id"; // remove on success
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarkFailedAsync(int id, string error, int attempts, TimeSpan? retryAfter = null)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE transaction_staging
                SET status = 'Failed', last_error = $error, attempts = $attempts, next_retry_utc = $nextRetry
                WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$error", error ?? string.Empty);
            cmd.Parameters.AddWithValue("$attempts", attempts);
            cmd.Parameters.AddWithValue("$nextRetry", retryAfter.HasValue ? DateTime.UtcNow.Add(retryAfter.Value).ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetPendingCountAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(1) FROM transaction_staging WHERE status IN ('Pending','Failed')";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}
