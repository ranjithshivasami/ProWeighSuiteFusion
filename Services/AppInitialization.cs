using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services
{
    public static class AppInitialization
    {
        public static Task Task { get; set; } = Task.CompletedTask;
    }
}
