using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Auditing;
using ProWeighSuitePlatforms.Core.Branding;
using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;
using ProWeighSuitePlatforms.Core.Updates;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Core;

public class ModuleRegistrationTests
{
    [Fact]
    public void Build_RegistersFakeModuleContributions()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .AddModule(new FakeModule())
            .Build();

        var module = Assert.Single(host.Modules.Modules);
        Assert.Equal("fake", module.Metadata.ModuleId);
        Assert.Single(host.Menus.All);
        Assert.Single(host.Routes.All);
        Assert.Single(host.Permissions.All);
        Assert.True(host.Permissions.Contains("fake.view"));
        Assert.Single(host.Settings.All);
        Assert.Single(host.Migrations.All);
        Assert.Single(host.ProductModules.All);
        Assert.Equal("fake.product", host.ProductModules.All[0].ModuleId);
        Assert.Equal("fake", host.Services.GetRequiredService<FakeService>().Name);
    }

    [Fact]
    public void Build_RegistersCoreServicesThroughDependencyInjection()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder().Build();

        Assert.Same(host.Modules, host.Services.GetRequiredService<IModuleRegistry>());
        Assert.Same(host.ProductModules, host.Services.GetRequiredService<IProductModuleRegistry>());
        Assert.Same(host.Menus, host.Services.GetRequiredService<IMenuRegistry>());
        Assert.Same(host.Routes, host.Services.GetRequiredService<IRouteRegistry>());
        Assert.Same(host.Permissions, host.Services.GetRequiredService<IPermissionRegistry>());
        Assert.Same(host.Settings, host.Services.GetRequiredService<ISettingsRegistry>());
        Assert.Same(host.Migrations, host.Services.GetRequiredService<IMigrationRegistry>());
        Assert.NotNull(host.Services.GetRequiredService<ISettingsService>());
        Assert.NotNull(host.Services.GetRequiredService<IAuditService>());
        Assert.NotNull(host.Services.GetRequiredService<ISessionContext>());
        Assert.NotNull(host.Services.GetRequiredService<IAuthorizationService>());
        Assert.NotNull(host.Services.GetRequiredService<IBrandingProvider>());
        Assert.NotNull(host.Services.GetRequiredService<ILicenseService>());
        Assert.NotNull(host.Services.GetRequiredService<IUpdateService>());
    }

    [Fact]
    public async Task DefaultSettingsService_StoresAndReturnsValues()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder().Build();
        var settings = host.Services.GetRequiredService<ISettingsService>();

        await settings.SetValueAsync("fake.enabled", "true");

        Assert.Equal("true", await settings.GetValueAsync("fake.enabled"));
    }

    [Fact]
    public void Build_RejectsDuplicatePermissionKey()
    {
        var first = new ActionModule("first", context =>
            context.Permissions.Add(new PermissionDefinition("shared.view", "First", "Tests")));
        var second = new ActionModule("second", context =>
            context.Permissions.Add(new PermissionDefinition("shared.view", "Second", "Tests")));

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(first).AddModule(second).Build());

        Assert.Contains(exception.Errors, error => error.Contains("Duplicate permission key", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsDuplicateProductModuleId()
    {
        var first = new ActionModule("first", context =>
            context.ProductModules.Add(new ProductModuleDefinition("shared", "First", ProductModuleGroups.Core)));
        var second = new ActionModule("second", context =>
            context.ProductModules.Add(new ProductModuleDefinition("shared", "Second", ProductModuleGroups.Core)));

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(first).AddModule(second).Build());

        Assert.Contains(exception.Errors, error => error.Contains("Duplicate product module ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsMenuWithMissingRoute()
    {
        var module = new ActionModule("fake", context =>
            context.Menus.Add(new MenuItemDefinition("fake.menu", "Fake", RouteId: "missing.route")));

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(module).Build());

        Assert.Contains(exception.Errors, error => error.Contains("missing route", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsMenuWithMissingPermission()
    {
        var module = new ActionModule("fake", context =>
        {
            context.Routes.Add(new RouteMetadata("fake.route", "Fake"));
            context.Menus.Add(new MenuItemDefinition(
                "fake.menu",
                "Fake",
                RouteId: "fake.route",
                RequiredPermissions: new[] { "missing.permission" }));
        });

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(module).Build());

        Assert.Contains(exception.Errors, error => error.Contains("missing permission", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsMissingModuleDependency()
    {
        var module = new FakeModule("fake", new[] { "missing" });

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(module).Build());

        Assert.Contains(exception.Errors, error => error.Contains("missing module", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsModuleDependencyCycle()
    {
        var first = new FakeModule("first", new[] { "second" });
        var second = new FakeModule("second", new[] { "first" });

        var exception = Assert.Throws<ModuleValidationException>(() =>
            new ProWeighSuitePlatformsHostBuilder().AddModule(first).AddModule(second).Build());

        Assert.Contains(exception.Errors, error => error.Contains("dependency cycle", StringComparison.Ordinal));
    }

    private sealed class FakeModule : IModule
    {
        public FakeModule()
            : this("fake", Array.Empty<string>())
        {
        }

        public FakeModule(string moduleId, string[] dependsOn)
        {
            Metadata = new ModuleMetadata(
                moduleId,
                "Fake Module",
                new Version(1, 0),
                new Version(1, 0),
                DependsOn: dependsOn);
        }

        public ModuleMetadata Metadata { get; }

        public void Register(IModuleRegistrationContext context)
        {
            context.ProductModules.Add(new ProductModuleDefinition(
                "fake.product",
                "Fake Product",
                ProductModuleGroups.Workspace));
            context.Permissions.Add(new PermissionDefinition("fake.view", "View fake", "Fake", Metadata.ModuleId));
            context.Menus.Add(new MenuItemDefinition(
                "fake.menu",
                "Fake",
                RouteId: "fake.route",
                RequiredPermissions: new[] { "fake.view" }));
            context.Routes.Add(new RouteMetadata(
                "fake.route",
                "Fake",
                RequiredPermissions: new[] { "fake.view" },
                ModuleId: Metadata.ModuleId));
            context.Settings.Add(new SettingDefinition(
                "fake.enabled",
                "Enabled",
                "true",
                "Fake",
                Metadata.ModuleId));
            context.Migrations.Add(new FakeMigration(Metadata.ModuleId));
            context.Services.AddSingleton<FakeService>();
        }
    }

    private sealed class ActionModule : IModule
    {
        private readonly Action<IModuleRegistrationContext> _register;

        public ActionModule(string moduleId, Action<IModuleRegistrationContext> register)
        {
            _register = register;
            Metadata = new ModuleMetadata(
                moduleId,
                moduleId,
                new Version(1, 0),
                new Version(1, 0));
        }

        public ModuleMetadata Metadata { get; }

        public void Register(IModuleRegistrationContext context) => _register(context);
    }

    private sealed class FakeMigration : IModuleMigration
    {
        public FakeMigration(string moduleId)
        {
            ModuleId = moduleId;
        }

        public string Id => $"{ModuleId}.migration";

        public string ModuleId { get; }

        public int Order => 1;

        public Task ApplyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeService
    {
        public string Name => "fake";
    }
}
