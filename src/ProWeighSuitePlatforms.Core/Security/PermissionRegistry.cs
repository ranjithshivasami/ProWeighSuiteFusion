using System;
using System.Collections.Generic;
using System.Linq;
using ProWeighSuitePlatforms.Core.Navigation;

namespace ProWeighSuitePlatforms.Core.Security;

internal sealed class PermissionRegistry : IPermissionRegistry, IPermissionRegistryBuilder
{
    private readonly List<PermissionDefinition> _items = new();

    public IReadOnlyList<PermissionDefinition> All => _items;

    public void Add(PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _items.Add(definition);
    }

    public bool Contains(string key) =>
        _items.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    public PermissionDefinition? Find(string key) =>
        _items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
}
