using System.Data;

namespace ProWeighSuitePlatforms.Data.Interfaces
{
    /// <summary>
    /// Factory interface for creating database connections.
    /// Allows switching between different database providers (SQLite, MSSQL, etc.)
    /// </summary>
    public interface IDbConnectionFactory
    {
        /// <summary>
        /// Creates and returns a new database connection
        /// </summary>
        IDbConnection CreateConnection();
        
        /// <summary>
        /// Gets the database provider type (SQLite, MSSQL, etc.)
        /// </summary>
        string ProviderName { get; }
        
        /// <summary>
        /// Gets the connection string
        /// </summary>
        string ConnectionString { get; }
    }
}
