using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Settings;

public sealed class InMemorySettingsService : ISettingsService
{
    private readonly ConcurrentDictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _values[key] = value;
        return Task.CompletedTask;
    }
}
