using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Auditing;
using ProWeighSuitePlatforms.Core.Branding;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Adapters;

public sealed class LegacyBrandingAdapter : IBrandingProvider
{
    private readonly ProWeighSuitePlatforms.Modules.Contracts.IBrandingProvider? _inner;

    public LegacyBrandingAdapter(ProWeighSuitePlatforms.Modules.Contracts.IBrandingProvider? inner)
    {
        _inner = inner;
    }

    public string ProductName => _inner?.Brand ?? "ProWeigh";

    public string Title => _inner?.Title ?? "ProWeigh Suite";

    public string ThemeName => _inner?.Brand ?? "Standard";

    public string PrimaryColor => _inner?.PrimaryHex ?? "#FF9F00";

    public string SecondaryColor => _inner?.SecondaryHex ?? "#009688";

    public string LogoPath => _inner?.LogoRelativePath ?? string.Empty;
}

public sealed class LegacyLicenseAdapter : ILicenseService
{
    private readonly ProWeighSuitePlatforms.Modules.Contracts.ILicenseService? _inner;
    private readonly IReadOnlyCollection<string>? _buildEntitledModules;

    public LegacyLicenseAdapter(
        ProWeighSuitePlatforms.Modules.Contracts.ILicenseService? inner,
        IReadOnlyCollection<string>? buildEntitledModules = null)
    {
        _inner = inner;
        _buildEntitledModules = buildEntitledModules;
    }

    public Task<bool> IsAllowedAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        if (_buildEntitledModules is not null &&
            _buildEntitledModules.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(true);
        }

        return _inner is null
            ? Task.FromResult(true)
            : _inner.IsAllowedAsync(moduleId, cancellationToken);
    }

    public Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default) =>
        _inner is null
            ? Task.FromResult(false)
            : _inner.IsExpiredAsync(cancellationToken);
}

public sealed class LegacyAuditAdapter : IAuditService
{
    private AuditService? _inner;

    public void Attach(AuditService service) => _inner = service;

    public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var inner = _inner;
        if (inner is null)
            return;

        var entry = new AuditEntry
        {
            Action = auditEvent.Action,
            Category = ParseCategory(auditEvent.Category),
            Username = auditEvent.UserName ?? string.Empty,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            Details = auditEvent.Details ?? string.Empty,
            OldValue = auditEvent.OldValue,
            NewValue = auditEvent.NewValue,
            Severity = ParseSeverity(auditEvent.Severity),
            Timestamp = auditEvent.Timestamp?.UtcDateTime ?? DateTime.UtcNow
        };

        await inner.LogAsync(entry).ConfigureAwait(false);
    }

    private static AuditCategory ParseCategory(string value) =>
        Enum.TryParse<AuditCategory>(value, ignoreCase: true, out var category)
            ? category
            : AuditCategory.System;

    private static AuditSeverity ParseSeverity(string value) =>
        Enum.TryParse<AuditSeverity>(value, ignoreCase: true, out var severity)
            ? severity
            : AuditSeverity.Info;
}

public sealed class AppSettingsAdapter : ISettingsService
{
    private readonly AppSettingsService _inner;

    public AppSettingsAdapter(AppSettingsService inner)
    {
        _inner = inner;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var settings = await _inner.LoadAsync().ConfigureAwait(false);
        return key.ToLowerInvariant() switch
        {
            "brand.primaryhex" => settings.BrandPrimaryHex,
            "brand.themename" => settings.BrandThemeName,
            _ => null
        };
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var settings = await _inner.LoadAsync().ConfigureAwait(false);
        switch (key.ToLowerInvariant())
        {
            case "brand.primaryhex":
                settings.BrandPrimaryHex = value;
                settings.BrandThemeCustom = true;
                break;
            case "brand.themename":
                settings.BrandThemeName = value;
                settings.BrandThemeCustom = true;
                break;
            default:
                return;
        }

        await _inner.SaveAsync(settings).ConfigureAwait(false);
    }
}

public sealed class LegacySessionContext : ISessionContext, ProWeighSuitePlatforms.Core.Security.ICurrentUserContext
{
    private readonly object _gate = new();
    private User? _currentUser;

    public event EventHandler? Changed;

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
                return _currentUser is not null;
        }
    }

    public string? UserName
    {
        get
        {
            lock (_gate)
                return _currentUser?.Username;
        }
    }

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            lock (_gate)
                return _currentUser is null
                    ? Array.Empty<string>()
                    : new[] { _currentUser.Role.ToString() };
        }
    }

    public User? CurrentUser
    {
        get
        {
            lock (_gate)
                return _currentUser;
        }
    }

    public void SetUser(User? user)
    {
        lock (_gate)
            _currentUser = user;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class LegacyAuthorizationAdapter : IAuthorizationService
{
    private readonly IPermissionRegistry _permissions;
    private readonly LegacySessionContext _session;
    private readonly AuthorizationService _authorization;

    public LegacyAuthorizationAdapter(
        IPermissionRegistry permissions,
        LegacySessionContext session,
        AuthorizationService authorization)
    {
        _permissions = permissions;
        _session = session;
        _authorization = authorization;
    }

    public async Task<bool> IsAllowedAsync(
        string permissionKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
            return false;

        var user = _session.CurrentUser;
        if (user is null)
            return false;

        var definition = _permissions.Find(permissionKey);
        var legacyKey = definition?.LegacyKey ?? permissionKey;

        if (string.IsNullOrWhiteSpace(legacyKey) ||
            !Enum.TryParse<Permission>(legacyKey, ignoreCase: true, out var permission))
        {
            return false;
        }

        return await _authorization
            .HasPermissionAsync(user, permission, cancellationToken)
            .ConfigureAwait(false);
    }
}
