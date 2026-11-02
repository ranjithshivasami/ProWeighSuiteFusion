using System;

namespace ProWeighSuitePlatforms.Core.Wpf.Navigation
{
    public static class ProductNavigationRequests
    {
        public static event Action<string, string?>? ShowRouteRequested;

        public static void RequestShowRoute(string routeId, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return;

            ShowRouteRequested?.Invoke(routeId, context);
        }
    }
}
