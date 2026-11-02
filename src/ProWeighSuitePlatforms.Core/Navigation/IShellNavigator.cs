using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Wpf.Navigation;

public interface IShellNavigator
{
    Task<bool> NavigateToRouteAsync(string routeId);
}
