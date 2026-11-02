using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProWeighSuitePlatforms.Behaviors
{
    /// <summary>
    /// Attached behavior that automatically adjusts the view when the touch keyboard is shown
    /// to prevent input controls from being obscured by the keyboard.
    /// </summary>
    public static class TouchKeyboardBehavior
    {
        #region Win32 API Imports
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        #region Attached Properties

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(TouchKeyboardBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public static readonly DependencyProperty ScrollViewerProperty =
            DependencyProperty.RegisterAttached(
                "ScrollViewer",
                typeof(ScrollViewer),
                typeof(TouchKeyboardBehavior),
                new PropertyMetadata(null));

        public static ScrollViewer GetScrollViewer(DependencyObject obj)
        {
            return (ScrollViewer)obj.GetValue(ScrollViewerProperty);
        }

        public static void SetScrollViewer(DependencyObject obj, ScrollViewer value)
        {
            obj.SetValue(ScrollViewerProperty, value);
        }

        public static readonly DependencyProperty AutoOpenKeyboardProperty =
            DependencyProperty.RegisterAttached(
                "AutoOpenKeyboard",
                typeof(bool),
                typeof(TouchKeyboardBehavior),
                new PropertyMetadata(false));

        public static bool GetAutoOpenKeyboard(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoOpenKeyboardProperty);
        }

        public static void SetAutoOpenKeyboard(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoOpenKeyboardProperty, value);
        }

        #endregion

        #region Private Fields

        private static DispatcherTimer? _keyboardDetectionTimer;
        private static Window? _attachedWindow;
        private static FrameworkElement? _currentFocusedElement;
        private static double _originalVerticalOffset;
        private static ScrollViewer? _activeScrollViewer;
        private static bool _isKeyboardVisible;
        private static double _lastScreenHeight;

        #endregion

        #region Behavior Activation

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.Loaded += OnElementLoaded;
                    element.Unloaded += OnElementUnloaded;
                }
                else
                {
                    element.Loaded -= OnElementLoaded;
                    element.Unloaded -= OnElementUnloaded;
                    CleanupBehavior();
                }
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _attachedWindow = Window.GetWindow(element);
                if (_attachedWindow != null)
                {
                    InitializeBehavior(element);
                }
            }
        }

        private static void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            CleanupBehavior();
        }

        #endregion

        #region Initialization and Cleanup

        private static void InitializeBehavior(FrameworkElement element)
        {
            // Store the initial screen height
            _lastScreenHeight = SystemParameters.PrimaryScreenHeight;

            // Set up keyboard detection timer
            _keyboardDetectionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _keyboardDetectionTimer.Tick += OnKeyboardDetectionTimer;
            _keyboardDetectionTimer.Start();

            // Subscribe to focus events on all input controls
            SubscribeToFocusEvents(element);
        }

        private static void CleanupBehavior()
        {
            _keyboardDetectionTimer?.Stop();
            _keyboardDetectionTimer = null;

            if (_attachedWindow != null)
            {
                UnsubscribeFromFocusEvents(_attachedWindow);
            }

            RestoreOriginalLayout();

            _attachedWindow = null;
            _currentFocusedElement = null;
            _activeScrollViewer = null;
            _isKeyboardVisible = false;
        }

        #endregion

        #region Focus Event Handling

        private static void SubscribeToFocusEvents(DependencyObject element)
        {
            // Find all text input controls and subscribe to their focus events
            var controls = FindVisualChildren<Control>(element);
            foreach (var control in controls)
            {
                if (IsInputControl(control))
                {
                    control.GotFocus += OnInputControlGotFocus;
                    control.LostFocus += OnInputControlLostFocus;
                }
            }
        }

        private static void UnsubscribeFromFocusEvents(DependencyObject element)
        {
            var controls = FindVisualChildren<Control>(element);
            foreach (var control in controls)
            {
                if (IsInputControl(control))
                {
                    control.GotFocus -= OnInputControlGotFocus;
                    control.LostFocus -= OnInputControlLostFocus;
                }
            }
        }

        private static bool IsInputControl(Control control)
        {
            return control is TextBox || 
                   control is PasswordBox || 
                   control is ComboBox ||
                   control is TextBoxBase;
        }

        private static void OnInputControlGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _currentFocusedElement = element;
                
                // Find the parent ScrollViewer
                _activeScrollViewer = FindParentScrollViewer(element);
                if (_activeScrollViewer == null)
                {
                    // Check if a ScrollViewer was explicitly set
                    _activeScrollViewer = GetScrollViewer(element);
                }

                if (_activeScrollViewer != null)
                {
                    _originalVerticalOffset = _activeScrollViewer.VerticalOffset;
                }

                // Auto-open keyboard if enabled
                bool autoOpen = GetAutoOpenKeyboard(element);
                if (autoOpen)
                {
                    Services.TouchKeyboardHelper.OpenKeyboard();
                }

                // Delay the adjustment to allow keyboard to appear
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (_isKeyboardVisible)
                    {
                        AdjustLayoutForKeyboard();
                    }
                };
                timer.Start();
            }
        }

        private static void OnInputControlLostFocus(object sender, RoutedEventArgs e)
        {
            // Check if focus moved to another input control
            var newFocus = Keyboard.FocusedElement as FrameworkElement;
            if (newFocus is not Control ctrl || !IsInputControl(ctrl))
            {
                _currentFocusedElement = null;
            }
        }

        #endregion

        #region Keyboard Detection

        private static void OnKeyboardDetectionTimer(object? sender, EventArgs e)
        {
            bool keyboardVisible = IsKeyboardVisible();
            
            if (keyboardVisible != _isKeyboardVisible)
            {
                _isKeyboardVisible = keyboardVisible;
                
                if (_isKeyboardVisible && _currentFocusedElement != null)
                {
                    AdjustLayoutForKeyboard();
                }
                else if (!_isKeyboardVisible)
                {
                    RestoreOriginalLayout();
                }
            }
        }

        private static bool IsKeyboardVisible()
        {
            if (_attachedWindow == null) return false;

            try
            {
                // Method 1: Check for screen height change (works for docked keyboard)
                double currentScreenHeight = SystemParameters.WorkArea.Height;
                double fullScreenHeight = SystemParameters.PrimaryScreenHeight;
                
                // If work area is significantly smaller than screen, keyboard is likely visible
                if (fullScreenHeight - currentScreenHeight > 100)
                {
                    return true;
                }

                // Method 2: Look for touch keyboard window
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(foregroundWindow, out rect))
                    {
                        int windowHeight = rect.Bottom - rect.Top;
                        int windowWidth = rect.Right - rect.Left;
                        
                        // Touch keyboard is typically 200-400px tall and wide
                        if (windowHeight > 150 && windowHeight < 500 && 
                            windowWidth > 500 && rect.Bottom > fullScreenHeight * 0.5)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Layout Adjustment

        private static void AdjustLayoutForKeyboard()
        {
            if (_currentFocusedElement == null || _attachedWindow == null) return;

            try
            {
                // Get the keyboard height
                double keyboardHeight = GetKeyboardHeight();
                if (keyboardHeight <= 0) return;

                // Get the position of the focused element relative to the window
                var elementPosition = _currentFocusedElement.TransformToAncestor(_attachedWindow)
                    .Transform(new Point(0, 0));
                
                double elementBottom = elementPosition.Y + _currentFocusedElement.ActualHeight;
                
                // Get the visible area (window height minus keyboard height)
                double visibleHeight = _attachedWindow.ActualHeight - keyboardHeight;
                
                // Calculate how much we need to scroll
                double requiredOffset = elementBottom - visibleHeight + 50; // 50px padding

                if (requiredOffset > 0)
                {
                    if (_activeScrollViewer != null)
                    {
                        // Scroll to make the element visible
                        double newOffset = _activeScrollViewer.VerticalOffset + requiredOffset;
                        _activeScrollViewer.ScrollToVerticalOffset(newOffset);
                    }
                    else
                    {
                        // If no ScrollViewer, try to move the window
                        if (_attachedWindow.WindowState == WindowState.Normal)
                        {
                            double newTop = _attachedWindow.Top - requiredOffset;
                            if (newTop >= 0)
                            {
                                _attachedWindow.Top = newTop;
                            }
                        }
                    }
                }

                // Ensure the focused element is brought into view
                _currentFocusedElement.BringIntoView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adjusting layout for keyboard: {ex.Message}");
            }
        }

        private static void RestoreOriginalLayout()
        {
            if (_activeScrollViewer != null)
            {
                // Smoothly scroll back to original position
                _activeScrollViewer.ScrollToVerticalOffset(_originalVerticalOffset);
            }
        }

        private static double GetKeyboardHeight()
        {
            // Calculate keyboard height based on screen work area
            double fullScreenHeight = SystemParameters.PrimaryScreenHeight;
            double workAreaHeight = SystemParameters.WorkArea.Height;
            
            double keyboardHeight = fullScreenHeight - workAreaHeight;
            
            // If no change in work area, use default keyboard height estimate
            if (keyboardHeight < 50)
            {
                // Default touch keyboard is about 30% of screen height
                keyboardHeight = fullScreenHeight * 0.3;
            }
            
            return keyboardHeight;
        }

        #endregion

        #region Helper Methods

        private static ScrollViewer? FindParentScrollViewer(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
            }
            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T t)
                    {
                        yield return t;
                    }

                    if (child == null) continue;
                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        #endregion
    }
}
