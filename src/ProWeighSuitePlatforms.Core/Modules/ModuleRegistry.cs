using System;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Core.Modules;

internal sealed class ModuleRegistry : IModuleRegistry
{
    private readonly IReadOnlyList<IModule> _modules;

    public ModuleRegistry(IEnumerable<IModule> modules)
    {
        _modules = modules.ToList();
        Metadata = _modules.Select(module => module.Metadata).ToList();
    }

    public IReadOnlyList<IModule> Modules => _modules;

    public IReadOnlyList<ModuleMetadata> Metadata { get; }

    public IModule? Find(string moduleId) =>
        _modules.FirstOrDefault(module =>
            string.Equals(module.Metadata.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
}
