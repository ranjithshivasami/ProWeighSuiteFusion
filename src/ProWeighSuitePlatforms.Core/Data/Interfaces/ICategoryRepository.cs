using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Data.Interfaces
{
    /// <summary>
    /// Repository interface for Category entities
    /// Inherits common CRUD operations from IRepository<Category>
    /// </summary>
    public interface ICategoryRepository : IRepository<Category>
    {
        // Note: GetAllAsync, UpdateAsync, DeleteAsync are inherited from IRepository<Category>
        // Only declare methods that are specific to Category or have different signatures

        Task<Category?> GetByIdAsync(int id);
        Task<int> AddAsync(Category category);
    }
}
