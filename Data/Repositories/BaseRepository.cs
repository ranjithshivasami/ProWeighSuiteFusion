using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data.Interfaces;

namespace ProWeighSuitePlatforms.Data.Repositories
{
    /// <summary>
    /// Base repository class with common database operations
    /// Provides database-agnostic functionality for all repositories
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    public abstract class BaseRepository<T> : IRepository<T> where T : class
    {
        protected readonly DatabaseContext _dbContext;

        protected BaseRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Creates a new database connection using the configured provider
        /// </summary>
        protected IDbConnection GetConnection()
        {
            return _dbContext.CreateConnection();
        }

        /// <summary>
        /// Gets the database provider name (SQLite, MSSQL, etc.)
        /// </summary>
        protected string ProviderName => _dbContext.ProviderName;

        /// <summary>
        /// Checks if current provider is SQLite
        /// </summary>
        protected bool IsSQLite => ProviderName == "SQLite";

        /// <summary>
        /// Checks if current provider is MSSQL
        /// </summary>
        protected bool IsMSSQL => ProviderName == "MSSQL";

        // Abstract methods to be implemented by derived classes
        public abstract Task<int> CreateAsync(T entity);
        public abstract Task<T?> GetAsync(int id);
        public abstract Task<IEnumerable<T>> GetAllAsync();
        public abstract Task<int> UpdateAsync(T entity);
        public abstract Task<int> DeleteAsync(int id);
    }
}
