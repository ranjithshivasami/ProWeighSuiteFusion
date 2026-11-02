using System.Data;
using System.IO;
using ProWeighSuitePlatforms.Data.Interfaces;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data.Factories
{
    /// <summary>
    /// SQLite implementation of the database connection factory
    /// </summary>
    public class SqliteConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public SqliteConnectionFactory(string dbPath)
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbPath}";
        }

        public IDbConnection CreateConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        public string ProviderName => "SQLite";

        public string ConnectionString => _connectionString;
    }
}
