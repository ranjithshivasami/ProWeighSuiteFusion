using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Data
{
    public interface IProductDatabaseInitializer
    {
        string Name { get; }

        Task InitializeAsync(string dbPath);
    }
}
