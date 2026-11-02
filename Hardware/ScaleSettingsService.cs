using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class ScaleSettingsService
    {
        private readonly ScaleSettingsRepository _repo;
        public ScaleSettingsService(ScaleSettingsRepository repo)
        {
            _repo = repo;
        }

        public Task InitializeAsync() => _repo.InitializeAsync();
        public Task<ScaleSettings?> GetAsync() => _repo.GetAsync();
        public Task<ScaleSettings?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
        public Task<List<ScaleSettings>> GetAllAsync() => _repo.GetAllAsync();
        public Task<int> AddAsync(ScaleSettings settings) => _repo.AddAsync(settings);
        public Task<int> UpdateAsync(ScaleSettings settings) => _repo.UpdateAsync(settings);
        public Task<int> DeleteAsync(int id) => _repo.DeleteAsync(id);
        public Task SaveAsync(ScaleSettings settings) => _repo.SaveAsync(settings);
        public Task<bool> PortExistsAsync(string portName, int? excludeId = null) => _repo.PortExistsAsync(portName, excludeId);
    }
}
