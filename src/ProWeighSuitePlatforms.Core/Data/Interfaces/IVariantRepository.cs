using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Data.Interfaces
{
    public interface IVariantRepository
    {
        Task InitializeAsync();
        Task<List<Variant>> GetAllAsync();
        Task<Variant?> GetByIdAsync(int id);
        Task<int> AddAsync(Variant variant);
        Task<int> UpdateAsync(Variant variant);
        Task<int> DeleteAsync(int id);
        Task<bool> NameExistsAsync(string name, int? excludeId = null);
    }
}
