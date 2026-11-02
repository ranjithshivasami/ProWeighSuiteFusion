using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Navigation;

internal sealed class MenuRegistry : IMenuRegistry, IMenuRegistryBuilder
{
    private readonly List<MenuItemDefinition> _items = new();

    public IReadOnlyList<MenuItemDefinition> All => _items;

    public void Add(MenuItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _items.Add(definition);
    }

    public MenuItemDefinition? Find(string id) =>
        _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
}
