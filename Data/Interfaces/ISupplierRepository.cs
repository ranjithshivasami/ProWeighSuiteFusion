using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Data.Interfaces
{
    /// <summary>
    /// Repository interface for Supplier entities.
    /// </summary>
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<Supplier?> GetByIdAsync(int id);
        Task<int> AddAsync(Supplier supplier);
    }
}
