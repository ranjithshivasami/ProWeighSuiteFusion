using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ShellView : UserControl
    {

        public ShellView()
        {
            InitializeComponent();

            // Subscribe to LoadingService to show/hide overlay
            LoadingService.Instance.PropertyChanged += OnLoadingServicePropertyChanged;

            Unloaded += (s, e) =>
            {
                // Unsubscribe when view is unloaded
                LoadingService.Instance.PropertyChanged -= OnLoadingServicePropertyChanged;
            };
        }

        private void OnLoadingServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadingService.IsLoading))
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingOverlay.Visibility = LoadingService.Instance.IsLoading 
                        ? Visibility.Visible 
                        : Visibility.Collapsed;
                });
            }
            else if (e.PropertyName == nameof(LoadingService.LoadingMessage))
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingText.Text = LoadingService.Instance.LoadingMessage;
                });
            }
        }

        private void ButtonOpenMenu_Click(object sender, RoutedEventArgs e)
        {
            ButtonOpenMenu.Visibility = Visibility.Collapsed;
            ButtonCloseMenu.Visibility = Visibility.Visible;
            SmallLogoImage.Visibility = Visibility.Collapsed;
            LogoImage.Visibility = Visibility.Visible;
        }

        private void ButtonCloseMenu_Click(object sender, RoutedEventArgs e)
        {
            ButtonCloseMenu.Visibility = Visibility.Collapsed;
            ButtonOpenMenu.Visibility = Visibility.Visible;
            SmallLogoImage.Visibility = Visibility.Visible;
            LogoImage.Visibility = Visibility.Collapsed;
        }

        private void MenuItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Keep the main menu expanded when clicking a navigation item.
            // Only expand the drawer if it is currently collapsed; never collapse
            // on a child item click.
            if (GridMenu.Width == 60)
            {
                ExpandMenu();
            }
            // Do NOT set e.Handled or manipulate IsSubmenuOpen here so grouped
            // popup menus keep their normal behavior.
        }

        private void ExpandMenu()
        {
            // Trigger the open menu animation
            var openMenuStoryboard = (Storyboard)this.Resources["OpenMenu"];
            openMenuStoryboard.Begin();
            
            // Update button and logo visibility
            ButtonOpenMenu.Visibility = Visibility.Collapsed;
            ButtonCloseMenu.Visibility = Visibility.Visible;
            SmallLogoImage.Visibility = Visibility.Collapsed;
            LogoImage.Visibility = Visibility.Visible;
        }
    }
}