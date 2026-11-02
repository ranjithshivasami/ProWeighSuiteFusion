// Cross-VM navigation for Fusion screens (static-event pattern, same as
// QueueMonitorViewModel.RefreshRequested). Lets e.g. batch creation switch to
// the live view without referencing the Shell.
using System;

namespace ProWeighSuitePlatforms.Navigation
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

    /// <summary>
    /// Route IDs for the Fusion screens. Kept as constants so view models and
    /// the shell refer to the same strings without a route registry.
    /// </summary>
    public static class FusionRoutes
    {
        public const string Dashboard = "FusionDashboard";
        public const string Settings = "FusionSettings";
        public const string BayStatus = "BayStatus";
        public const string SupplierMaster = "SupplierMaster";
        public const string TubMaster = "TubMaster";
        public const string InwardStock = "InwardStock";
        public const string BatchLots = "BatchLots";
        public const string BatchStatus = "BatchStatus";
        public const string LiveView = "LiveView";
        public const string Reports = "FusionReports";
        public const string Category = "Category";
        public const string Variant = "Variant";
        public const string Materials = "Materials";
    }
}
