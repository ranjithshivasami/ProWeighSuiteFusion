using System;

namespace ProWeighSuitePlatforms.Core.Navigation;

public sealed class NavigationChangeNotifier : INavigationChangeNotifier
{
    public event EventHandler<NavigationChangedEventArgs>? Changed;

    public void Notify(NavigationChangeReason reason)
    {
        Changed?.Invoke(this, new NavigationChangedEventArgs(reason));
    }
}
