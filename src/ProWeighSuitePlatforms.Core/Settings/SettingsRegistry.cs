using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Settings;

internal sealed class SettingsRegistry : ISettingsRegistry, ISettingsRegistryBuilder
{
    private readonly List<SettingDefinition> _items = new();

    public IReadOnlyList<SettingDefinition> All => _items;

    public void Add(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _items.Add(definition);
    }

    public SettingDefinition? Find(string key) =>
        _items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
}
