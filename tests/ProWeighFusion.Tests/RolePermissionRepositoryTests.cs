using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using Xunit;

namespace ProWeighSuitePlatforms.Tests;

public sealed class RolePermissionRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        "ProWeighSuitePlatforms.Tests",
        $"{Guid.NewGuid():N}.db");

    private RolePermissionRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new RolePermissionRepository(_dbPath);
        await _repository.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_SeedsDefaultPermissionsForEveryRole()
    {
        var map = await _repository.GetRolePermissionsMapAsync();

        Assert.Equal(3, map.Count);
        Assert.Contains(Permission.EditModules, map[UserRole.SuperAdmin]);
        Assert.Contains(Permission.ViewModules, map[UserRole.Admin]);
        Assert.DoesNotContain(Permission.EditModules, map[UserRole.Admin]);
        Assert.DoesNotContain(Permission.EditModules, map[UserRole.Operator]);

        // Submenu permissions are seeded per role so the matrix can grant or
        // revoke each menu independently.
        Assert.Contains(Permission.ViewAppSettings, map[UserRole.SuperAdmin]);
        Assert.Contains(Permission.ViewCleanUpDatabase, map[UserRole.SuperAdmin]);
        Assert.Contains(Permission.ViewAppSettings, map[UserRole.Admin]);
        Assert.DoesNotContain(Permission.ViewCleanUpDatabase, map[UserRole.Admin]);
        Assert.Contains(Permission.ViewFusionReports, map[UserRole.SuperAdmin]);
        Assert.Contains(Permission.ViewFusionReports, map[UserRole.Admin]);
        Assert.Contains(Permission.ViewFusionReports, map[UserRole.Operator]);
        Assert.Contains(Permission.ViewLiveView, map[UserRole.Operator]);
        Assert.DoesNotContain(Permission.ViewFusionSettings, map[UserRole.Operator]);
    }

    [Fact]
    public async Task UpdatePermissionAsync_ChangesGrantedMap()
    {
        await _repository.UpdatePermissionAsync(
            UserRole.SuperAdmin,
            Permission.EditModules,
            false,
            "test");

        var revoked = await _repository.GetRolePermissionsMapAsync();
        Assert.DoesNotContain(Permission.EditModules, revoked[UserRole.SuperAdmin]);

        await _repository.UpdatePermissionAsync(
            UserRole.SuperAdmin,
            Permission.EditModules,
            true,
            "test");

        var restored = await _repository.GetRolePermissionsMapAsync();
        Assert.Contains(Permission.EditModules, restored[UserRole.SuperAdmin]);
    }
}
