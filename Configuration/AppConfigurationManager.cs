using System;
using System.IO;
using ProWeighSuitePlatforms.Data.Configuration;

namespace ProWeighSuitePlatforms.Configuration
{
    /// <summary>
    /// Application configuration manager
    /// Handles database and application settings
    /// </summary>
    public class AppConfigurationManager
    {
        private static DatabaseConfiguration? _databaseConfig;

        /// <summary>
        /// Gets the current database configuration
        /// </summary>
        public static DatabaseConfiguration GetDatabaseConfiguration()
        {
            if (_databaseConfig != null)
                return _databaseConfig;

            // Default configuration for SQLite
            // Use roaming (AppData\Roaming) and the filename 'proweigh_suite_fusion.db' where current transactions are stored
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion",
                "proweigh_suite_fusion.db"
            );

            _databaseConfig = new DatabaseConfiguration
            {
                Provider = DatabaseProvider.SQLite,
                ConnectionString = dbPath,
                AutoInitialize = true,
                CommandTimeout = 30
            };

            return _databaseConfig;
        }

        /// <summary>
        /// Returns only the database file path for convenience
        /// </summary>
        public static string GetDatabasePath()
        {
            return GetDatabaseConfiguration().ConnectionString;
        }

        /// <summary>
        /// Sets a custom database configuration
        /// </summary>
        public static void SetDatabaseConfiguration(DatabaseConfiguration configuration)
        {
            _databaseConfig = configuration;
        }

        /// <summary>
        /// Loads configuration for MSSQL (for future migration)
        /// </summary>
        public static DatabaseConfiguration GetMssqlConfiguration(
            string server,
            string database,
            string? username = null,
            string? password = null,
            bool integratedSecurity = true)
        {
            string connectionString;

            if (integratedSecurity)
            {
                connectionString = $"Server={server};Database={database};Integrated Security=true;TrustServerCertificate=true;";
            }
            else
            {
                connectionString = $"Server={server};Database={database};User Id={username};Password={password};TrustServerCertificate=true;";
            }

            return new DatabaseConfiguration
            {
                Provider = DatabaseProvider.MSSQL,
                ConnectionString = connectionString,
                AutoInitialize = true,
                CommandTimeout = 30
            };
        }
    }
}
