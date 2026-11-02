using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public static class DbUtils
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _cache = new();

        public static async Task<bool> TableExistsAsync(string dbPath, string tableName)
        {
            if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(tableName)) return false;
            var key = dbPath + "|" + tableName;
            if (_cache.TryGetValue(key, out var cached) && cached) return true;
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1";
                cmd.Parameters.AddWithValue("$name", tableName);
                var result = await cmd.ExecuteScalarAsync();
                var exists = result != null && result != System.DBNull.Value;
                if (exists) _cache[key] = true;
                return exists;
            }
            catch
            {
                return false;
            }
        }
    }
}
