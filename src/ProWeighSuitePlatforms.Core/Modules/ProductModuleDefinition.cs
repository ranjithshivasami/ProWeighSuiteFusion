namespace ProWeighSuitePlatforms.Core.Modules;

public sealed record ProductModuleDefinition(
    string ModuleId,
    string DisplayName,
    string GroupName,
    string? ParentModuleId = null,
    string IconKey = "",
    string RequiredCapability = "None",
    string Description = "",
    bool IsLicenseGated = true,
    bool ShowInModuleMatrix = true);

public static class ProductModuleGroups
{
    public const string Core = "Core";
    public const string Weighing = "Weighing";
    public const string Inventory = "Inventory";
    public const string Workspace = "Workspace";
}
