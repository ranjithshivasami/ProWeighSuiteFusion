using System.Collections.Generic;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.ViewModels
{
    // Phase 3: presentation model for registry-driven shell navigation. The
    // command still delegates to the existing screen commands until the content
    // path is converted to view factories.
    public sealed class ShellNavigationItem : BaseViewModel
    {
        public ShellNavigationItem(
            string id,
            string title,
            string? iconKey,
            string? routeId,
            ICommand? command,
            object? commandParameter = null,
            bool isCompact = false,
            IReadOnlyList<ShellNavigationItem>? children = null)
        {
            Id = id;
            Title = title;
            IconKey = iconKey;
            RouteId = routeId;
            Command = command;
            CommandParameter = commandParameter;
            IsCompact = isCompact;
            Children = children ?? System.Array.Empty<ShellNavigationItem>();
            Icon = CreateIcon(iconKey);
        }

        public string Id { get; }

        public string Title { get; }

        public string? IconKey { get; }

        public string? RouteId { get; }

        public ICommand? Command { get; }

        public object? CommandParameter { get; }

        public bool IsCompact { get; }

        public IReadOnlyList<ShellNavigationItem> Children { get; }

        public bool HasChildren => Children.Count > 0;

        public object? Icon { get; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private static object? CreateIcon(string? iconKey)
        {
            if (string.IsNullOrWhiteSpace(iconKey) ||
                !System.Enum.TryParse<MaterialDesignThemes.Wpf.PackIconKind>(
                    iconKey, true, out var kind))
            {
                return null;
            }

            return new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = kind,
                Width = 20,
                Height = 20
            };
        }
    }
}
