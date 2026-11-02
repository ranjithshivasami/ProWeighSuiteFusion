using System;
using System.Collections.Generic;
using ProWeighSuitePlatforms.Core.Navigation;

namespace ProWeighSuitePlatforms.Core.Wpf.Views;

public sealed record ViewFactoryRegistration(
    string RouteId,
    Func<IServiceProvider, object> ViewModelFactory,
    Func<object, object> ViewFactory,
    RouteCacheMode CacheMode = RouteCacheMode.Transient);

public interface IViewFactoryRegistry
{
    IReadOnlyList<ViewFactoryRegistration> All { get; }

    ViewFactoryRegistration? Find(string routeId);
}

public interface IViewFactoryRegistryBuilder
{
    void Register(ViewFactoryRegistration registration);
}

public interface IViewFactory
{
    object CreateView(string routeId, IServiceProvider services);
}

public sealed class ViewFactoryRegistry : IViewFactoryRegistry, IViewFactoryRegistryBuilder, IViewFactory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ViewFactoryRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _keepAlive =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ViewFactoryRegistration> All
    {
        get
        {
            lock (_gate)
                return new List<ViewFactoryRegistration>(_registrations.Values);
        }
    }

    public void Register(ViewFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.RouteId))
            throw new ArgumentException("Route ID is required.", nameof(registration));

        lock (_gate)
            _registrations[registration.RouteId] = registration;
    }

    public void ClearCache()
    {
        lock (_gate)
            _keepAlive.Clear();
    }

    public ViewFactoryRegistration? Find(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return null;

        lock (_gate)
            return _registrations.TryGetValue(routeId, out var registration)
                ? registration
                : null;
    }

    public object CreateView(string routeId, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registration = Find(routeId)
            ?? throw new InvalidOperationException($"No view factory is registered for route '{routeId}'.");

        return registration.CacheMode switch
        {
            RouteCacheMode.Transient => Create(registration, services),
            RouteCacheMode.KeepAlive or RouteCacheMode.Singleton => CreateCached(registration, services),
            _ => Create(registration, services)
        };
    }

    private object CreateCached(ViewFactoryRegistration registration, IServiceProvider services)
    {
        lock (_gate)
        {
            if (_keepAlive.TryGetValue(registration.RouteId, out var cached))
                return cached;

            var view = Create(registration, services);
            _keepAlive[registration.RouteId] = view;
            return view;
        }
    }

    private static object Create(ViewFactoryRegistration registration, IServiceProvider services)
    {
        var viewModel = registration.ViewModelFactory(services)
            ?? throw new InvalidOperationException(
                $"The view-model factory for route '{registration.RouteId}' returned null.");

        return registration.ViewFactory(viewModel)
            ?? throw new InvalidOperationException(
                $"The view factory for route '{registration.RouteId}' returned null.");
    }
}
