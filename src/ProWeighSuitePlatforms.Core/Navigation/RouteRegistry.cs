using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Navigation;

internal sealed class RouteRegistry : IRouteRegistry, IRouteRegistryBuilder
{
    private readonly List<RouteMetadata> _items = new();

    public IReadOnlyList<RouteMetadata> All => _items;

    public void Add(RouteMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _items.Add(metadata);
    }

    public RouteMetadata? Find(string routeId) =>
        _items.FirstOrDefault(item => string.Equals(item.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
}
