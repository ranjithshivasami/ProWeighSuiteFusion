// Diagnostic aid: periodically records GDI/USER object counts and the raw
// handle count of the process. Win32Exception 1816 ("Not enough quota is
// available to process this command") is USER/GDI quota exhaustion, and this
// log tells us which class of object is leaking when it happens.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ProWeighSuitePlatforms.Infrastructure
{
    public static class ProcessResourceMonitor
    {
        private const uint GrGdiObjects = 0;
        private const uint GrUserObjects = 1;

        private static readonly object Gate = new();
        private static int _ticksSinceLastLog;
        private const int LogEveryTicks = 60; // 2 s ticks * 60 = ~2 minutes

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessHandleCount(IntPtr hProcess, out uint count);

        /// <summary>Call from an existing periodic timer; logs at most every ~2 minutes.</summary>
        public static void Tick()
        {
            lock (Gate)
            {
                _ticksSinceLastLog++;
                if (_ticksSinceLastLog < LogEveryTicks) return;
                _ticksSinceLastLog = 0;
            }
            LogSnapshot("periodic");
        }

        /// <summary>Immediately appends a snapshot line to %AppData%\ProWeighSuitePlatforms\resources.log.</summary>
        public static void LogSnapshot(string reason)
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                var gdi = GetGuiResources(p.Handle, GrGdiObjects);
                var user = GetGuiResources(p.Handle, GrUserObjects);
                GetProcessHandleCount(p.Handle, out var handles);
                var line = $"[{DateTime.Now:O}] {reason}: handles={handles} gdi={gdi} user={user}";
                System.Diagnostics.Debug.WriteLine($"[Resources] {line}");
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuitePlatforms");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "resources.log"), line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never crash the app.
            }
        }
    }
}
