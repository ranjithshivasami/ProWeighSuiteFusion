using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class LoginView : UserControl
    {
        private bool _passwordVisible;
        private bool _passwordTextUpdating;
        private DispatcherTimer? _statusTimer;
        private int _statusCheckRunning;
        private const string OnlineColor = "#0bb14d";
        private const string OfflineColor = "#d93025";

        public LoginView()
        {
            InitializeComponent();
            Loaded += LoginView_Loaded;
            Unloaded += LoginView_Unloaded;
        }

        private void LoginView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer = null;
            }
        }

        private string GetPassword() => _passwordVisible ? pwdText.Text : pwd.Password;

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_passwordVisible && !_passwordTextUpdating)
            {
                _passwordTextUpdating = true;
                pwdText.Text = pwd.Password;
                pwdText.CaretIndex = pwdText.Text.Length;
                _passwordTextUpdating = false;
            }
        }

        private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_passwordVisible || _passwordTextUpdating) return;
            _passwordTextUpdating = true;
            pwd.Password = pwdText.Text;
            _passwordTextUpdating = false;
        }

        private void OnTogglePassword(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                pwdText.Text = pwd.Password;
                pwd.Visibility = Visibility.Collapsed;
                pwdText.Visibility = Visibility.Visible;
                eyeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOff;
                pwdText.Focus();
                pwdText.CaretIndex = pwdText.Text.Length;
            }
            else
            {
                pwd.Password = pwdText.Text;
                pwdText.Visibility = Visibility.Collapsed;
                pwd.Visibility = Visibility.Visible;
                eyeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Eye;
                pwd.Focus();
            }
        }

        private void LoginView_Loaded(object? sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                try
                {
                    if (UsernameTextBox != null && UsernameTextBox.IsVisible && UsernameTextBox.IsEnabled)
                    {
                        UsernameTextBox.Focus();
                        Keyboard.Focus(UsernameTextBox);
                    }
                }
                catch
                {
                    // ignore
                }
            }));

            Loaded -= LoginView_Loaded;
            _ = UpdateInternetStatusAsync();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _statusTimer.Tick += (_, _) => _ = UpdateInternetStatusAsync();
            _statusTimer.Start();
        }

        private async Task UpdateInternetStatusAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _statusCheckRunning, 1) == 1) return;
            try
            {
                var online = await Task.Run(async () =>
                {
                    try { using var ping = new Ping(); var reply = await ping.SendPingAsync("8.8.8.8", 2500); return reply.Status == IPStatus.Success; }
                    catch { return false; }
                });
                var color = online ? OnlineColor : OfflineColor;
                if (new System.Windows.Media.BrushConverter().ConvertFromString(color) is System.Windows.Media.Brush brush)
                    StatusEllipse.Fill = brush;
                StatusText.Text = online ? "Online" : "Offline";
                StatusText.Foreground = StatusEllipse.Fill;
            }
            catch { }
            finally { System.Threading.Interlocked.Exchange(ref _statusCheckRunning, 0); }
        }

        private void OnLoginClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.Password = GetPassword();
                if (vm.LoginCommand?.CanExecute(null) == true)
                {
                    vm.LoginCommand.Execute(null);
                }
            }
        }

        private void OnOtpVerifyClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
            {
                if (vm.LoginCommand?.CanExecute(null) == true)
                {
                    vm.LoginCommand.Execute(null);
                }
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var window = Window.GetWindow(this);
            if (window == null || window.WindowState == WindowState.Maximized) return;
            try { window.DragMove(); } catch { }
        }

        private void OnUsernameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_passwordVisible) pwdText.Focus(); else pwd.Focus();
                e.Handled = true;
            }
        }

        private void OnPasswordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnLoginClick(sender, e);
                e.Handled = true;
            }
        }

        private void OnOtpKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOtpVerifyClick(sender, e);
                e.Handled = true;
            }
        }
    }
}
