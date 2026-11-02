namespace ProWeighSuitePlatforms.Data.Configuration
{
    /// <summary>
    /// Database provider types
    /// </summary>
    public enum DatabaseProvider
    {
        SQLite,
        MSSQL
    }

    /// <summary>
    /// Database configuration settings
    /// </summary>
    public class DatabaseConfiguration
    {
        /// <summary>
        /// Database provider to use
        /// </summary>
        public DatabaseProvider Provider { get; set; } = DatabaseProvider.SQLite;

        /// <summary>
        /// Connection string or database path
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Whether to auto-initialize database schema
        /// </summary>
        public bool AutoInitialize { get; set; } = true;

        /// <summary>
        /// Command timeout in seconds
        /// </summary>
        public int CommandTimeout { get; set; } = 30;
    }
}
