using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProWeighSuitePlatforms.Behaviors
{
    public static class AutoFocusBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(AutoFocusBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe)
            {
                if ((bool)e.NewValue)
                {
                    fe.Loaded += FrameworkElement_Loaded;
                    fe.IsVisibleChanged += FrameworkElement_IsVisibleChanged;
                }
                else
                {
                    fe.Loaded -= FrameworkElement_Loaded;
                    fe.IsVisibleChanged -= FrameworkElement_IsVisibleChanged;
                }
            }
        }

        private static void FrameworkElement_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.IsVisible)
            {
                StartFocusAttempts(fe);
            }
        }

        private static void FrameworkElement_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                StartFocusAttempts(fe);
            }
        }

        private static void StartFocusAttempts(FrameworkElement fe)
        {
            // Try immediate first at Render priority
            fe.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (TryFocusOnce(fe)) return;

                // If immediate attempt didn't succeed, start timer retrying a few times
                var attempts = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };

                EventHandler? handler = null;
                handler = (s, args) =>
                {
                    attempts++;
                    if (TryFocusOnce(fe) || attempts >= 8)
                    {
                        timer.Stop();
                        if (handler != null)
                            timer.Tick -= handler;
                    }
                };

                timer.Tick += handler;
                timer.Start();
            }));
        }

        private static bool TryFocusOnce(FrameworkElement fe)
        {
            try
            {
                // If something already has keyboard focus and it's an input control, do nothing
                var focused = Keyboard.FocusedElement as FrameworkElement;
                if (focused != null)
                {
                    if (focused is TextBox || focused is PasswordBox || focused is ComboBox || focused is DatePicker)
                        return true; // already focused
                }

                var first = FindFirstFocusable(fe);
                if (first != null)
                {
                    if (first.Focus())
                    {
                        Keyboard.Focus(first);
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static FrameworkElement? FindFirstFocusable(DependencyObject parent)
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe)
                {
                    if (fe.IsVisible && fe.IsEnabled && fe.Focusable)
                    {
                        // Prioritize standard input controls
                        if (fe is TextBox || fe is PasswordBox || fe is ComboBox || fe is DatePicker)
                            return fe;

                        // Otherwise keep searching deeper for input controls
                    }

                    var result = FindFirstFocusable(fe);
                    if (result != null) return result;

                    if (fe.IsVisible && fe.IsEnabled && fe.Focusable)
                        return fe;
                }
            }

            return null;
        }
    }
}
