using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Data
{
    public interface IProductDatabaseInitializer
    {
        string Name { get; }

        Task InitializeAsync(string dbPath);
    }
}
