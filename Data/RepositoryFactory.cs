using System;
using ProWeighSuitePlatforms.Data.Configuration;
using ProWeighSuitePlatforms.Data.Interfaces;
using ProWeighSuitePlatforms.Data.Repositories;

namespace ProWeighSuitePlatforms.Data
{
    /// <summary>
    /// Service factory for creating repository instances
    /// Manages dependency injection and repository lifecycle
    /// </summary>
    public class RepositoryFactory
    {
        private readonly DatabaseContext _dbContext;
        private static RepositoryFactory? _instance;
        private static readonly object _lock = new object();

        private RepositoryFactory(DatabaseConfiguration configuration)
        {
            _dbContext = new DatabaseContext(configuration);
        }

        /// <summary>
        /// Initializes the repository factory with database configuration
        /// </summary>
        public static void Initialize(DatabaseConfiguration configuration)
        {
            lock (_lock)
            {
                _instance = new RepositoryFactory(configuration);
            }
        }

        /// <summary>
        /// Gets the singleton instance of the repository factory
        /// </summary>
        public static RepositoryFactory Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        "RepositoryFactory has not been initialized. Call Initialize() first.");
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the database context
        /// </summary>
        public DatabaseContext DbContext => _dbContext;

        /// <summary>
        /// Creates a new instance of ICategoryRepository
        /// </summary>
        public ICategoryRepository CreateCategoryRepository()
        {
            var dbPath = _dbContext.ConnectionString.Replace("Data Source=", "");
            return new CategoryRepository(dbPath);
        }

        /// <summary>
        /// Creates a new instance of IMaterialRepository
        /// </summary>
        public IMaterialRepository CreateMaterialRepository()
        {
            // Return the existing implementation for now
            // Later, refactor MaterialRepository to use DatabaseContext
            return new MaterialRepository(_dbContext.ConnectionString.Replace("Data Source=", ""));
        }

        /// <summary>
        /// Gets the current database provider name
        /// </summary>
        public string CurrentProvider => _dbContext.ProviderName;
    }
}
