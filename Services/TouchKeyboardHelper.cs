using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Helper service to programmatically open and close the Windows touch keyboard.
    /// </summary>
    public static class TouchKeyboardHelper
    {
        #region Win32 API Imports

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;

        private const string TouchKeyboardClassName = "IPTip_Main_Window";
        private const string TabTipPath = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";

        #endregion

        /// <summary>
        /// Opens the Windows touch keyboard.
        /// </summary>
        /// <returns>True if keyboard was opened or is already visible.</returns>
        public static bool OpenKeyboard()
        {
            try
            {
                // Check if keyboard is already visible
                IntPtr keyboardHandle = FindWindow(TouchKeyboardClassName, null);
                if (keyboardHandle != IntPtr.Zero && IsWindowVisible(keyboardHandle))
                {
                    System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Keyboard already visible");
                    return true;
                }

                // Launch TabTip.exe to open the touch keyboard
                if (File.Exists(TabTipPath))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = TabTipPath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        if (process != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[TouchKeyboard] TabTip.exe launched");
                            
                            // Wait a bit for the keyboard to appear
                            System.Threading.Thread.Sleep(100);
                            
                            // Show the keyboard window if it was minimized
                            keyboardHandle = FindWindow(TouchKeyboardClassName, null);
                            if (keyboardHandle != IntPtr.Zero)
                            {
                                ShowWindow(keyboardHandle, SW_SHOW);
                                System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Keyboard opened successfully");
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] TabTip.exe not found at: {TabTipPath}");
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Error opening keyboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Closes the Windows touch keyboard if it's visible.
        /// </summary>
        /// <returns>True if keyboard was closed or is already hidden.</returns>
        public static bool CloseKeyboard()
        {
            try
            {
                IntPtr keyboardHandle = FindWindow(TouchKeyboardClassName, null);
                
                if (keyboardHandle != IntPtr.Zero && IsWindowVisible(keyboardHandle))
                {
                    ShowWindow(keyboardHandle, SW_HIDE);
                    System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Keyboard closed");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Keyboard already hidden or not found");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Error closing keyboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the Windows touch keyboard is currently visible.
        /// </summary>
        /// <returns>True if keyboard is visible.</returns>
        public static bool IsKeyboardVisible()
        {
            try
            {
                IntPtr keyboardHandle = FindWindow(TouchKeyboardClassName, null);
                return keyboardHandle != IntPtr.Zero && IsWindowVisible(keyboardHandle);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enables the touch keyboard to automatically appear when touching input fields.
        /// This modifies Windows registry settings.
        /// </summary>
        /// <returns>True if setting was applied successfully.</returns>
        public static bool EnableAutomaticInvocation()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\TabletTip\1.7"))
                {
                    if (key != null)
                    {
                        key.SetValue("EnableDesktopModeAutoInvoke", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Automatic invocation enabled");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Error enabling automatic invocation: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Disables the touch keyboard from automatically appearing.
        /// This modifies Windows registry settings.
        /// </summary>
        /// <returns>True if setting was applied successfully.</returns>
        public static bool DisableAutomaticInvocation()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\TabletTip\1.7"))
                {
                    if (key != null)
                    {
                        key.SetValue("EnableDesktopModeAutoInvoke", 0, Microsoft.Win32.RegistryValueKind.DWord);
                        System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Automatic invocation disabled");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TouchKeyboard] Error disabling automatic invocation: {ex.Message}");
            }

            return false;
        }
    }
}
