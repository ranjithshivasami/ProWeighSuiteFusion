using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Settings;

public sealed record SettingDefinition(
    string Key,
    string DisplayName,
    string? DefaultValue = null,
    string? Group = null,
    string? ModuleId = null);

public interface ISettingsRegistry
{
    IReadOnlyList<SettingDefinition> All { get; }

    SettingDefinition? Find(string key);
}

public interface ISettingsRegistryBuilder
{
    void Add(SettingDefinition definition);
}

public interface ISettingsService
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
}
