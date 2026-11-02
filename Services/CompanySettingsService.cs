using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class CompanySettingsService
    {
        private readonly CompanySettingsRepository _repo;

        public CompanySettingsService(CompanySettingsRepository repo)
        {
            _repo = repo;
        }

        public Task InitializeAsync() => _repo.InitializeAsync();
        public Task<CompanySettings?> GetAsync() => _repo.GetAsync();
        public Task<int> SaveAsync(CompanySettings settings) => _repo.SaveAsync(settings);
    }
}
