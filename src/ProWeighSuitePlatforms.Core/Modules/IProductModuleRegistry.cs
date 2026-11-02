using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Modules;

public interface IProductModuleRegistry
{
    IReadOnlyList<ProductModuleDefinition> All { get; }

    ProductModuleDefinition? Find(string moduleId);
}

public interface IProductModuleRegistryBuilder
{
    void Add(ProductModuleDefinition definition);
}
