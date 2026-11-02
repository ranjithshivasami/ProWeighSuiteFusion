using System;
using System.Data;
// using System.Data.SqlClient; // Uncomment when adding System.Data.SqlClient package
using ProWeighSuitePlatforms.Data.Interfaces;

namespace ProWeighSuitePlatforms.Data.Factories
{
    /// <summary>
    /// MSSQL implementation of the database connection factory
    /// This will be used in the next release when migrating to MSSQL
    /// NOTE: Requires System.Data.SqlClient NuGet package to be installed
    /// </summary>
    public class MssqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public MssqlConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection()
        {
            // TODO: Uncomment when System.Data.SqlClient package is installed
            // return new SqlConnection(_connectionString);
            throw new NotImplementedException(
                "MSSQL support requires System.Data.SqlClient NuGet package. " +
                "Install the package and uncomment the SqlConnection code.");
        }

        public string ProviderName => "MSSQL";

        public string ConnectionString => _connectionString;
    }
}
