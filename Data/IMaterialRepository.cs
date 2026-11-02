using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Data
{
    public interface IMaterialRepository
    {
        Task InitializeAsync();
        Task<int> CreateAsync(Material material);
        Task<Material?> GetAsync(int id);
        Task<Material?> GetByCodeAsync(string productCode);
        Task<IEnumerable<Material>> GetAllAsync();
        Task<int> UpdateAsync(Material material);
        Task<int> DeleteAsync(int id);
        Task<bool> ProductCodeExistsAsync(string productCode, int? excludeId = null);
        Task<string> GenerateNextProductCodeAsync();
        Task<string?> GetCompanyMaterialCodePrefixAsync();
    }
}