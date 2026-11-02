using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    public sealed class ShellNavigator : IShellNavigator
    {
        private readonly ShellContentViewContext _context;

        public ShellNavigator(ShellContentViewContext context)
        {
            _context = context;
        }

        public Task<bool> NavigateToRouteAsync(string routeId) =>
            _context.Shell?.NavigateToRouteAsync(routeId) ?? Task.FromResult(false);
    }
}
