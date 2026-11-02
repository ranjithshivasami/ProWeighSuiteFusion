using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class ReportScheduleService
    {
        private readonly ReportScheduleRepository _repo;

        public ReportScheduleService()
        {
            _repo = new ReportScheduleRepository(AppConfigurationManager.GetDatabasePath());
        }

        public ReportScheduleService(string dbPath)
        {
            _repo = new ReportScheduleRepository(dbPath);
        }

        public Task InitializeAsync() => _repo.InitializeAsync();
        public Task<ScheduledReportSettings?> GetAsync() => _repo.GetAsync();
        public Task<int> SaveAsync(ScheduledReportSettings settings) => _repo.SaveAsync(settings);
    }
}
