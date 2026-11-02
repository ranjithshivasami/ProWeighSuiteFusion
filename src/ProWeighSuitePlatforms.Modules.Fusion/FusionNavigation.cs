// Cross-VM navigation for Fusion screens (static-event pattern, same as
// QueueMonitorViewModel.RefreshRequested). Lets e.g. batch creation switch to
// the live view without referencing the Shell.
using System;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public static class FusionNavigation
    {
        /// <summary>
        /// Shown by the shell when the operator tries to navigate away while
        /// the Fusion auto-run is active.
        /// </summary>
        public const string NavigationLockedMessage =
            "Navigation is locked during an active batch. Please pause or complete the batch operation before navigating.";

        public static string? PendingBatchNo { get; private set; }

        public static void RequestShowModule(string moduleId, string? batchNo = null)
        {
            if (!string.IsNullOrWhiteSpace(batchNo))
                PendingBatchNo = batchNo;
            ProductNavigationRequests.RequestShowRoute(moduleId, batchNo);
        }

        public static string? TakePendingBatchNo()
        {
            var v = PendingBatchNo;
            PendingBatchNo = null;
            return v;
        }
    }
}
