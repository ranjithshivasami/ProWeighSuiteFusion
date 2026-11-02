using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Modules.Core;
using ProWeighSuitePlatforms.Modules.Fusion;
using ProWeighSuitePlatforms.Modules.Infrastructure;
using Xunit;

namespace ProWeighSuitePlatforms.Tests;

[Collection("ProductModuleCatalog")]
public sealed class ModuleAssignmentRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        "ProWeighFusion.Tests",
        $"{Guid.NewGuid():N}.db");

    private ModuleAssignmentRepository _repository = null!;

    public async Task InitializeAsync()
    {
        using (var host = new ProWeighSuitePlatformsHostBuilder()
                   .AddModule(new CoreNavigationModule())
                   .AddModule(new FusionModule())
                   .Build())
        {
            ProductModuleCatalog.Initialize(host.ProductModules.All);
        }

        _repository = new ModuleAssignmentRepository(_dbPath);
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
    public async Task InitializeAsync_SeedsRoleGrantsForCatalogWeighingDefinitions()
    {
        var map = await _repository.GetEnabledMapAsync();

        // Core groups are not role-gated, so only non-core definitions are seeded.
        foreach (var definition in ModuleAssignmentRepository.SeedDefinitions
                     .Where(item => item.GroupName != ModuleAssignmentRepository.GroupCore))
        {
            Assert.True(map.ContainsKey(definition.ModuleId), definition.ModuleId);
            Assert.Equal(3, map[definition.ModuleId].Count);
        }

        foreach (var definition in ModuleAssignmentRepository.SeedDefinitions
                     .Where(item => item.GroupName == ModuleAssignmentRepository.GroupCore))
        {
            Assert.False(map.ContainsKey(definition.ModuleId), definition.ModuleId);
        }
    }

    [Fact]
    public async Task SetRoleModuleAsync_DisablesModuleForOneRoleOnly()
    {
        await _repository.SetRoleModuleAsync(UserRole.Admin, "Users", true, "test");
        await _repository.SetRoleModuleAsync(UserRole.SuperAdmin, "Users", true, "test");
        await _repository.SetRoleModuleAsync(UserRole.Operator, "Users", false, "test");

        var map = await _repository.GetEnabledMapAsync();

        Assert.DoesNotContain(UserRole.Operator, map["Users"]);
        Assert.Contains(UserRole.Admin, map["Users"]);
        Assert.Contains(UserRole.SuperAdmin, map["Users"]);
    }

    [Fact]
    public async Task SetActiveWeighingModuleAsync_PersistsSelection()
    {
        await _repository.SetActiveWeighingModuleAsync(
            "Fusion",
            ModuleAssignmentRepository.WeighingOnly.Instance);

        Assert.Equal("Fusion", await _repository.GetActiveWeighingModuleAsync());
    }

    [Fact]
    public async Task GetActiveWeighingModuleAsync_ReturnsDefaultWhenUnset()
    {
        Assert.Equal(
            ModuleAssignmentRepository.DefaultActiveWeighing,
            await _repository.GetActiveWeighingModuleAsync());
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var before = await _repository.GetEnabledMapAsync();

        await _repository.InitializeAsync();
        var after = await _repository.GetEnabledMapAsync();

        Assert.Equal(before.Count, after.Count);
        foreach (var pair in before)
            Assert.Equal(pair.Value, after[pair.Key]);
    }
}
