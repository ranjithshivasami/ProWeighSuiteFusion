using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service for detecting and managing the touch keyboard state.
    /// Provides events and information about keyboard visibility.
    /// </summary>
    public class TouchKeyboardService
    {
        #region Win32 API Imports

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        #endregion

        #region Singleton

        private static TouchKeyboardService? _instance;
        private static readonly object _lock = new object();

        public static TouchKeyboardService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TouchKeyboardService();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        public event EventHandler<KeyboardVisibilityChangedEventArgs>? KeyboardVisibilityChanged;
        public event EventHandler<KeyboardBoundsChangedEventArgs>? KeyboardBoundsChanged;

        #endregion

        #region Properties

        private bool _isKeyboardVisible;
        public bool IsKeyboardVisible
        {
            get => _isKeyboardVisible;
            private set
            {
                if (_isKeyboardVisible != value)
                {
                    _isKeyboardVisible = value;
                    OnKeyboardVisibilityChanged(value);
                }
            }
        }

        private Rect _keyboardBounds;
        public Rect KeyboardBounds
        {
            get => _keyboardBounds;
            private set
            {
                if (_keyboardBounds != value)
                {
                    _keyboardBounds = value;
                    OnKeyboardBoundsChanged(value);
                }
            }
        }

        public double KeyboardHeight => IsKeyboardVisible ? KeyboardBounds.Height : 0;

        #endregion

        #region Fields

        private DispatcherTimer? _detectionTimer;
        private const int DetectionIntervalMs = 100;
        private static readonly string[] TouchKeyboardClassNames = 
        {
            "IPTip_Main_Window",        // Windows 10/11 touch keyboard
            "MSCTFIME UI",               // Alternative keyboard class
            "ApplicationFrameWindow"     // Sometimes used for keyboard
        };

        #endregion

#if DEBUG
        #region Debug Simulation Mode

        private bool _simulationEnabled = false;
        private bool _simulatedKeyboardVisible = false;
        private double _simulatedKeyboardHeight = 300;

        /// <summary>
        /// Enable simulation mode for debugging without a physical touch keyboard.
        /// Toggle with Ctrl+Shift+K shortcut.
        /// </summary>
        public bool SimulationEnabled
        {
            get => _simulationEnabled;
            set
            {
                _simulationEnabled = value;
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Simulation mode: {(value ? "ENABLED" : "DISABLED")}");
                if (!value)
                {
                    _simulatedKeyboardVisible = false;
                }
            }
        }

        /// <summary>
        /// Simulated keyboard height in debug mode (default 300px).
        /// </summary>
        public double SimulatedKeyboardHeight
        {
            get => _simulatedKeyboardHeight;
            set => _simulatedKeyboardHeight = Math.Max(100, Math.Min(600, value));
        }

        /// <summary>
        /// Manually show simulated keyboard (Debug only).
        /// Use Ctrl+Shift+K when simulation is enabled.
        /// </summary>
        public void ShowSimulatedKeyboard()
        {
            if (!_simulationEnabled) return;
            
            _simulatedKeyboardVisible = true;
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Simulated keyboard SHOWN");
            
            // Force immediate detection
            OnDetectionTimerTick(null, EventArgs.Empty);
        }

        /// <summary>
        /// Manually hide simulated keyboard (Debug only).
        /// Use Ctrl+Shift+K again when keyboard is visible.
        /// </summary>
        public void HideSimulatedKeyboard()
        {
            if (!_simulationEnabled) return;
            
            _simulatedKeyboardVisible = false;
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Simulated keyboard HIDDEN");
            
            // Force immediate detection
            OnDetectionTimerTick(null, EventArgs.Empty);
        }

        /// <summary>
        /// Toggle simulated keyboard visibility (Debug only).
        /// Bound to Ctrl+Shift+K shortcut.
        /// </summary>
        public void ToggleSimulatedKeyboard()
        {
            if (!_simulationEnabled)
            {
                SimulationEnabled = true;
                ShowSimulatedKeyboard();
            }
            else if (_simulatedKeyboardVisible)
            {
                HideSimulatedKeyboard();
            }
            else
            {
                ShowSimulatedKeyboard();
            }
        }

        #endregion
#endif

        #region Constructor

        private TouchKeyboardService()
        {
            _keyboardBounds = Rect.Empty;
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Service initialized");
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] DEBUG MODE: Press Ctrl+Shift+K to simulate keyboard");
#endif
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start monitoring for touch keyboard visibility changes.
        /// </summary>
        public void StartMonitoring()
        {
            if (_detectionTimer != null) return;

            _detectionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DetectionIntervalMs)
            };
            _detectionTimer.Tick += OnDetectionTimerTick;
            _detectionTimer.Start();

#if DEBUG
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Monitoring STARTED");
#endif
        }

        /// <summary>
        /// Stop monitoring for touch keyboard visibility changes.
        /// </summary>
        public void StopMonitoring()
        {
            _detectionTimer?.Stop();
            _detectionTimer = null;

#if DEBUG
            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Monitoring STOPPED");
#endif
        }

        /// <summary>
        /// Manually check if the touch keyboard is currently visible.
        /// </summary>
        public bool CheckKeyboardVisibility()
        {
            return DetectKeyboard(out _);
        }

        /// <summary>
        /// Get the current keyboard bounds in screen coordinates.
        /// </summary>
        public Rect GetKeyboardBounds()
        {
            DetectKeyboard(out Rect bounds);
            return bounds;
        }

        /// <summary>
        /// Calculate the available screen space (screen height minus keyboard height).
        /// </summary>
        public double GetAvailableScreenHeight()
        {
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            return IsKeyboardVisible ? screenHeight - KeyboardHeight : screenHeight;
        }

        #endregion

        #region Private Methods

        private void OnDetectionTimerTick(object? sender, EventArgs e)
        {
            bool wasVisible = IsKeyboardVisible;
            Rect oldBounds = KeyboardBounds;

            bool isVisible = DetectKeyboard(out Rect newBounds);

            // Update visibility
            IsKeyboardVisible = isVisible;

            // Update bounds if changed
            if (isVisible && !oldBounds.Equals(newBounds))
            {
                KeyboardBounds = newBounds;
            }
            else if (!isVisible)
            {
                KeyboardBounds = Rect.Empty;
            }

#if DEBUG
            if (wasVisible != isVisible)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Visibility changed: {isVisible}");
                if (isVisible)
                {
                    System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Keyboard bounds: {newBounds}");
                    System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Keyboard height: {newBounds.Height}px");
                }
            }
#endif
        }

        private bool DetectKeyboard(out Rect bounds)
        {
            bounds = Rect.Empty;

#if DEBUG
            // Use simulated keyboard in debug mode if enabled
            if (_simulationEnabled && _simulatedKeyboardVisible)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                
                bounds = new Rect(
                    0,
                    screenHeight - _simulatedKeyboardHeight,
                    screenWidth,
                    _simulatedKeyboardHeight
                );
                
                return true;
            }
#endif

            try
            {
                // Method 1: Try to find touch keyboard window by class name
                foreach (string className in TouchKeyboardClassNames)
                {
                    IntPtr keyboardHandle = FindWindow(className, null);
                    
                    if (keyboardHandle != IntPtr.Zero && IsWindowVisible(keyboardHandle))
                    {
                        if (GetWindowRect(keyboardHandle, out RECT rect))
                        {
                            // Verify this is likely the keyboard (size constraints)
                            if (rect.Height > 100 && rect.Height < 600 && rect.Width > 400)
                            {
                                bounds = new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
                                return true;
                            }
                        }
                    }
                }

                // Method 2: Check work area vs screen height
                double workAreaHeight = SystemParameters.WorkArea.Height;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double difference = screenHeight - workAreaHeight;

                if (difference > 100) // At least 100px difference suggests keyboard
                {
                    bounds = new Rect(
                        0, 
                        workAreaHeight, 
                        SystemParameters.PrimaryScreenWidth, 
                        difference
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Error detecting keyboard: {ex.Message}");
                return false;
            }
        }

        private void OnKeyboardVisibilityChanged(bool isVisible)
        {
            KeyboardVisibilityChanged?.Invoke(this, new KeyboardVisibilityChangedEventArgs(isVisible));
        }

        private void OnKeyboardBoundsChanged(Rect bounds)
        {
            KeyboardBoundsChanged?.Invoke(this, new KeyboardBoundsChangedEventArgs(bounds));
        }

        #endregion
    }

    #region Event Args

    public class KeyboardVisibilityChangedEventArgs : EventArgs
    {
        public bool IsVisible { get; }

        public KeyboardVisibilityChangedEventArgs(bool isVisible)
        {
            IsVisible = isVisible;
        }
    }

    public class KeyboardBoundsChangedEventArgs : EventArgs
    {
        public Rect Bounds { get; }

        public KeyboardBoundsChangedEventArgs(Rect bounds)
        {
            Bounds = bounds;
        }
    }

    #endregion
}
