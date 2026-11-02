using System;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Views;

namespace ProWeighSuitePlatforms.Core.Wpf.Navigation;

public interface INavigationService
{
    string? CurrentRouteId { get; }

    object? CurrentView { get; }

    event EventHandler<NavigationChangedEventArgs>? CurrentRouteChanged;

    event EventHandler<NavigationChangedEventArgs>? CurrentRouteInvalidated;

    Task<NavigationGuardResult> CanNavigateAsync(
        string routeId,
        CancellationToken cancellationToken = default);

    Task<bool> NavigateAsync(
        string routeId,
        CancellationToken cancellationToken = default);
}

public sealed class NavigationService : INavigationService, IDisposable
{
    private readonly IRouteGuard _guard;
    private readonly IViewFactory _viewFactory;
    private readonly IServiceProvider _services;
    private readonly INavigationChangeNotifier? _notifier;
    private readonly object _gate = new();

    public NavigationService(
        IRouteGuard guard,
        IViewFactory viewFactory,
        IServiceProvider services,
        INavigationChangeNotifier? notifier = null)
    {
        _guard = guard;
        _viewFactory = viewFactory;
        _services = services;
        _notifier = notifier;

        if (_notifier is not null)
            _notifier.Changed += OnNavigationChanged;
    }

    public event EventHandler<NavigationChangedEventArgs>? CurrentRouteChanged;

    public event EventHandler<NavigationChangedEventArgs>? CurrentRouteInvalidated;

    public string? CurrentRouteId { get; private set; }

    public object? CurrentView { get; private set; }

    public Task<NavigationGuardResult> CanNavigateAsync(
        string routeId,
        CancellationToken cancellationToken = default) =>
        _guard.CanEnterAsync(routeId, cancellationToken);

    public async Task<bool> NavigateAsync(
        string routeId,
        CancellationToken cancellationToken = default)
    {
        var result = await _guard.CanEnterAsync(routeId, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed)
            return false;

        var view = _viewFactory.CreateView(routeId, _services);

        lock (_gate)
        {
            CurrentRouteId = routeId;
            CurrentView = view;
        }

        CurrentRouteChanged?.Invoke(
            this,
            new NavigationChangedEventArgs(NavigationChangeReason.Manual));
        return true;
    }

    public async Task RevalidateCurrentRouteAsync(
        NavigationChangeReason reason,
        CancellationToken cancellationToken = default)
    {
        string? currentRouteId;
        lock (_gate)
            currentRouteId = CurrentRouteId;

        if (string.IsNullOrWhiteSpace(currentRouteId))
            return;

        var result = await _guard.CanEnterAsync(currentRouteId, cancellationToken).ConfigureAwait(false);
        if (result.IsAllowed)
            return;

        lock (_gate)
        {
            CurrentRouteId = null;
            CurrentView = null;
        }

        CurrentRouteInvalidated?.Invoke(
            this,
            new NavigationChangedEventArgs(reason));
    }

    public void Dispose()
    {
        if (_notifier is not null)
            _notifier.Changed -= OnNavigationChanged;
    }

    private async void OnNavigationChanged(object? sender, NavigationChangedEventArgs e)
    {
        try
        {
            await RevalidateCurrentRouteAsync(e.Reason).ConfigureAwait(false);
        }
        catch
        {
            // A failed revalidation must not crash the UI thread.
        }
    }
}
