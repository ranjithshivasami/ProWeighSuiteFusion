using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly TouchKeyboardService _keyboardService;

        public MainWindow()
        {
            InitializeComponent();
            Title = ProWeighSuitePlatforms.Configuration.BrandingInfo.Title;
            var brandIcon = ProWeighSuitePlatforms.Configuration.BrandingInfo.IconSource;
            if (brandIcon != null)
                Icon = brandIcon;
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            
            // Subscribe to login success event to maximize window
            viewModel.LoginSuccessful += OnLoginSuccessful;

            // Initialize touch keyboard service
            _keyboardService = TouchKeyboardService.Instance;
            _keyboardService.StartMonitoring();

            // Stop monitoring and close all secondary windows when main window closes
            Closing += OnMainWindowClosing;
            Closed += OnMainWindowClosed;

#if DEBUG
            // Add keyboard shortcut for simulation (Ctrl+Shift+K)
            PreviewKeyDown += OnPreviewKeyDown;
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("  TOUCH KEYBOARD DEBUG MODE");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("  Press Ctrl+Shift+K to simulate touch keyboard");
            System.Diagnostics.Debug.WriteLine("  - First press: Enable simulation & show keyboard");
            System.Diagnostics.Debug.WriteLine("  - Second press: Hide keyboard");
            System.Diagnostics.Debug.WriteLine("  - Third press: Show keyboard again");
            System.Diagnostics.Debug.WriteLine("═══════════════════════════════════════════════════════");
#endif
        }
        
        private void OnLoginSuccessful(object? sender, EventArgs e)
        {
            WindowState = WindowState.Maximized;
        }

        private bool _exitConfirmed;
        private bool _exitPromptOpen;

        private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
        {
            // Already confirmed via the password prompt — let the close proceed.
            if (_exitConfirmed) return;

            // Require password confirmation before closing (moved from ShellView's
            // custom close button now that the OS title bar owns window chrome).
            if (DataContext is MainViewModel main
                && main.CurrentViewModel is ShellViewModel shell
                && shell.CurrentUser != null)
            {
                // Always cancel first: the actual close happens after confirmation.
                e.Cancel = true;

                // Re-entrant close (double-click X, or Shutdown racing the prompt)
                // must not stack password dialogs or call Close() on a closing
                // window — both throw InvalidOperationException.
                if (_exitPromptOpen) return;
                _exitPromptOpen = true;
                var username = shell.CurrentUser.Username;
                try
                {
                    var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(this, username);
                    if (!passwordConfirmed)
                    {
                        var auditService = App.AuditService;
                        if (auditService != null)
                        {
                            await auditService.LogSecurityViolationAsync(
                                username, "Application Exit", "Password verification failed");
                        }
                        return;
                    }

                    _exitConfirmed = true;
                    Closing -= OnMainWindowClosing;
                    // Defer the re-close: if the awaits above completed synchronously
                    // we are still inside the original Closing event, and a re-entrant
                    // Close() is swallowed (InvalidOperationException), leaving the app
                    // open until the user clicks X again. Dispatching guarantees the
                    // close runs as a fresh request after this event completes.
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Close();
                        }
                        catch (InvalidOperationException)
                        {
                            // Shutdown already in progress — let it finish.
                        }
                    }), DispatcherPriority.Normal);
                }
                finally
                {
                    _exitPromptOpen = false;
                }
            }
        }

        /// <summary>
        /// Called by exit paths that already confirmed the password (e.g. the
        /// Exit menu command) so this window's Closing guard lets the shutdown
        /// through instead of prompting a second time.
        /// </summary>
        public void MarkExitConfirmed()
        {
            _exitConfirmed = true;
        }

        private void OnMainWindowClosed(object? sender, EventArgs e)
        {
            _keyboardService.StopMonitoring();

            // Close all secondary windows (Queue Monitor, Audit Log, Failed Sync Viewer, etc.)
            var secondaryWindows = Application.Current.Windows
                .OfType<Window>()
                .Where(w => w != this)
                .ToList();

            foreach (var window in secondaryWindows)
            {
                try { window.Close(); } catch { }
            }
        }

#if DEBUG
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Shift+K to toggle simulated keyboard
            if (e.Key == Key.K && 
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                _keyboardService.ToggleSimulatedKeyboard();
                e.Handled = true;
            }
        }
#endif
    }
}
