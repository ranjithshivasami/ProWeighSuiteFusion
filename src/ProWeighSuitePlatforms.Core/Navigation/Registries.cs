using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Navigation;

public interface IMenuRegistry
{
    IReadOnlyList<MenuItemDefinition> All { get; }

    MenuItemDefinition? Find(string id);
}

public interface IMenuRegistryBuilder
{
    void Add(MenuItemDefinition definition);
}

public interface IRouteRegistry
{
    IReadOnlyList<RouteMetadata> All { get; }

    RouteMetadata? Find(string routeId);
}

public interface IRouteRegistryBuilder
{
    void Add(RouteMetadata metadata);
}
