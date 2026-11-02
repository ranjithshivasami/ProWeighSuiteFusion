using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Modules;

public interface IModuleRegistry
{
    IReadOnlyList<IModule> Modules { get; }

    IReadOnlyList<ModuleMetadata> Metadata { get; }

    IModule? Find(string moduleId);
}
