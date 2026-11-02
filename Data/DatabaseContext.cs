using System;
using System.Data;
using ProWeighSuitePlatforms.Data.Configuration;
using ProWeighSuitePlatforms.Data.Factories;
using ProWeighSuitePlatforms.Data.Interfaces;

namespace ProWeighSuitePlatforms.Data
{
    /// <summary>
    /// Database context manager that provides database connections
    /// and manages database initialization
    /// </summary>
    public class DatabaseContext
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly DatabaseConfiguration _configuration;

        public DatabaseContext(DatabaseConfiguration configuration)
        {
            _configuration = configuration;
            _connectionFactory = CreateConnectionFactory(configuration);
        }

        /// <summary>
        /// Creates a new database connection
        /// </summary>
        public IDbConnection CreateConnection()
        {
            var connection = _connectionFactory.CreateConnection();
            return connection;
        }

        /// <summary>
        /// Gets the database provider name
        /// </summary>
        public string ProviderName => _connectionFactory.ProviderName;

        /// <summary>
        /// Gets the connection string
        /// </summary>
        public string ConnectionString => _connectionFactory.ConnectionString;

        /// <summary>
        /// Gets the database configuration
        /// </summary>
        public DatabaseConfiguration Configuration => _configuration;

        /// <summary>
        /// Creates the appropriate connection factory based on configuration
        /// </summary>
        private static IDbConnectionFactory CreateConnectionFactory(DatabaseConfiguration config)
        {
            return config.Provider switch
            {
                DatabaseProvider.SQLite => new SqliteConnectionFactory(config.ConnectionString),
                DatabaseProvider.MSSQL => new MssqlConnectionFactory(config.ConnectionString),
                _ => throw new NotSupportedException($"Database provider {config.Provider} is not supported")
            };
        }
    }
}
