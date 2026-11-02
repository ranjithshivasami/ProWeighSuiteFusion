using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Modules;

internal sealed class ProductModuleRegistry : IProductModuleRegistry, IProductModuleRegistryBuilder
{
    private readonly List<ProductModuleDefinition> _definitions = new();

    public IReadOnlyList<ProductModuleDefinition> All => _definitions;

    public void Add(ProductModuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions.Add(definition);
    }

    public ProductModuleDefinition? Find(string moduleId) =>
        _definitions.FirstOrDefault(definition =>
            string.Equals(definition.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
}
