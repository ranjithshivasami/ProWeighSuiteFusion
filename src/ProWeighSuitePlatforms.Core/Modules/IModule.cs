namespace ProWeighSuitePlatforms.Core.Modules;

public interface IModule
{
    ModuleMetadata Metadata { get; }

    void Register(IModuleRegistrationContext context);
}
