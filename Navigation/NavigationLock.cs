using System;

namespace ProWeighSuitePlatforms.Navigation
{
    /// <summary>
    /// Global navigation lock. A product module (e.g. Fusion live weighing)
    /// locks navigation while an operation is in progress so the operator
    /// stays on the owning screen; the shell checks the lock before switching
    /// routes and force-releases it on logout / exit.
    /// </summary>
    public static class NavigationLock
    {
        private static readonly object Gate = new();
        private static string? _ownerRouteId;
        private static string _message = string.Empty;
        private static Action? _forceRelease;

        /// <summary>
        /// Locks navigation away from <paramref name="ownerRouteId"/>. Re-selecting
        /// the owning route stays allowed (it is a no-op) so the owning screen
        /// remains reachable while the operation runs.
        /// </summary>
        public static void Lock(string ownerRouteId, string message, Action? forceRelease = null)
        {
            lock (Gate)
            {
                _ownerRouteId = ownerRouteId ?? string.Empty;
                _message = message ?? string.Empty;
                _forceRelease = forceRelease;
            }
        }

        public static void Unlock()
        {
            lock (Gate)
            {
                _ownerRouteId = null;
                _message = string.Empty;
                _forceRelease = null;
            }
        }

        /// <summary>True when navigation is currently locked.</summary>
        public static bool IsLocked
        {
            get { lock (Gate) { return _ownerRouteId != null; } }
        }

        public static bool TryGetLock(out string ownerRouteId, out string message)
        {
            lock (Gate)
            {
                ownerRouteId = _ownerRouteId ?? string.Empty;
                message = _message;
                return _ownerRouteId != null;
            }
        }

        /// <summary>
        /// Releases the lock and stops the owning operation. Used when the
        /// operator must leave the workspace entirely (logout / exit).
        /// </summary>
        public static void ForceRelease()
        {
            Action? release;
            lock (Gate)
            {
                release = _forceRelease;
                _ownerRouteId = null;
                _message = string.Empty;
                _forceRelease = null;
            }
            release?.Invoke();
        }
    }
}
