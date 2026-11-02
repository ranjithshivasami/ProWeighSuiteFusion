using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Migrations;

internal sealed class MigrationRegistry : IMigrationRegistry, IMigrationRegistryBuilder
{
    private readonly List<IModuleMigration> _items = new();

    public IReadOnlyList<IModuleMigration> All => _items;

    public void Add(IModuleMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        _items.Add(migration);
    }

    public IModuleMigration? Find(string id) =>
        _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
}
