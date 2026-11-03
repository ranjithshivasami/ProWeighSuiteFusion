// FUSION-PLAN.md Step 4 — plain data carriers for fusion_* rows (coconut/Fusion Link model).
// DataGrid binding only; no behavior, no weighing_* references.
using System;

namespace ProWeighSuitePlatforms.Models
{
    public sealed class FusionSupplier
    {
        public string Id { get; set; } = string.Empty;          // S001...
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;       // WhatsApp no (manual)
        public bool IsActive { get; set; } = true;
        public string StatusText => IsActive ? "Active" : "Inactive";
    }

    // One row per supplier x GRN for the supplier-master grid (mirrors fusion_link_v3_ui.html tab t1).
    public sealed class FusionSupplierStockRow
    {
        public string SupplierId { get; set; } = string.Empty;
        public string GrnNo { get; set; } = string.Empty;        // "-" when supplier has no GRN yet
        public string SupplierName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public double WbWeightKg { get; set; }
        public double WbCount { get; set; }
        public double ApwKg { get; set; }
        public double SampleKg { get; set; }
        public double SpoilKg { get; set; }
        public double WaterLossKg { get; set; }
        public double BrokenKg { get; set; }
        public double RejectionKg { get; set; }
        public int ShelfLifeDays { get; set; }
        public double AvailableKg { get; set; }                 // auto: WB minus 5 deductions
        public double BalanceNuts { get; set; }                 // auto
        public string Condition { get; set; } = string.Empty;   // Fresh / Near expiry / Expired / Consumed / No stock
        public bool SupplierActive { get; set; } = true;
        public bool HasGrn { get; set; }
    }

    public sealed class FusionTub
    {
        public string TubId { get; set; } = string.Empty;       // Tub_1...
        public double CapacityKg { get; set; }
        public string Status { get; set; } = string.Empty;
        public string BayId { get; set; } = string.Empty;
        // RFID / Fusion-Link extensions
        public string Epc { get; set; } = string.Empty;         // 00000320...
        public string Colour { get; set; } = "Orange";          // Orange / Blue
        public int UsedCycles { get; set; }
        public bool IsBlocked { get; set; }
        public string AgeingDate { get; set; } = string.Empty;  // yyyy-MM-dd
        public string AssignedTo { get; set; } = string.Empty;  // batch no (in-memory use)
        public string AssignedLot { get; set; } = string.Empty;

        public string DisplayStatus => IsBlocked ? "Blocked" : (!string.IsNullOrEmpty(AssignedTo) ? "In use" : "Available");
    }

    public sealed class FusionBatch
    {
        public string BatchNo { get; set; } = string.Empty;
        public double TargetKg { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        // Fusion-Link target extensions
        public string BatchDate { get; set; } = string.Empty;
        public string SupplierMode { get; set; } = string.Empty; // auto / single
        public string SupplierName { get; set; } = string.Empty;
        public string TubMode { get; set; } = string.Empty;      // colour / virtual
        public string LotMode { get; set; } = string.Empty;      // all / first / manual
        public double TargetApwKg { get; set; }
        public int NutsPerTub { get; set; }
        public double TargetHours { get; set; }
        public double TubLoopMin { get; set; }
        // Live run state
        public string StartedAt { get; set; } = string.Empty;
        public long PausedMs { get; set; }
        public string PauseStartedAt { get; set; } = string.Empty;
        public int PauseCount { get; set; }
        public string CompletedAt { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public int TargetTubs { get; set; }
        public int TargetNuts { get; set; }
    }

    // Batch status history for the live view stepper (Batch Created / Lot Created /
    // Running / Paused / Resumed / Batch Completed / Lot Completed ...).
    public sealed class FusionBatchEvent
    {
        public string BatchNo { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string At { get; set; } = string.Empty;
    }

    // FEFO plan lot (mirrors fusion_link_v3_ui.html tab t3 section 2).
    public sealed class FusionPlanLot
    {
        public string LotNo { get; set; } = string.Empty;       // LOT-1...
        public string BatchNo { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string GrnNo { get; set; } = string.Empty;
        public string BayId { get; set; } = string.Empty;
        public string BestBefore { get; set; } = string.Empty;
        public int DaysLeft { get; set; }
        public int Tubs { get; set; }
        public int Nuts { get; set; }
        public double WeightKg { get; set; }
        public string Colour { get; set; } = string.Empty;      // Orange / Blue / Mixed / Virtual
        public int TubsInPool { get; set; }
        public double CyclesPerTub { get; set; }
        // Live run progress
        public int DoneTubs { get; set; }
        public double DoneKg { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCompleted { get; set; }
    }

    public sealed class FusionLot
    {
        public string LotNo { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TubId { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }

    public sealed class FusionTransaction
    {
        public string Id { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TubId { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public double Weight { get; set; }
        public string Uom { get; set; } = "kg";
        public string RecordedAt { get; set; } = string.Empty;
        // Live ledger enrichment
        public string Epc { get; set; } = string.Empty;
        public int TubCycles { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string GrnNo { get; set; } = string.Empty;
        public string BayId { get; set; } = string.Empty;
        public string LotNo { get; set; } = string.Empty;
        public int Nuts { get; set; }
        public string OperatorName { get; set; } = string.Empty;
    }

    public sealed class FusionInward
    {
        public string GrnNo { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public double WeightKg { get; set; }
        public string ReceivedAt { get; set; } = string.Empty;
        // Fusion-Link extensions
        public string SupplierName { get; set; } = string.Empty;
        public string TicketNo { get; set; } = string.Empty;
        public string InwardDate { get; set; } = string.Empty;   // yyyy-MM-dd
        public string BayId { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public int ShelfLifeDays { get; set; } = 10;
        public double CoconutCount { get; set; }
        public double SampleKg { get; set; }
        public double SpoilKg { get; set; }
        public double WaterLossKg { get; set; }
        public double BrokenKg { get; set; }
        public double RejectionKg { get; set; }
        public double ConsumedKg { get; set; }
        public double ConsumedNuts { get; set; }
    }

    // Inward register row with derived stock maths (mirrors E() in fusion_link_v3_ui.html).
    public sealed class FusionInwardRow
    {
        public string GrnNo { get; set; } = string.Empty;
        public string InwardDate { get; set; } = string.Empty;
        public string TicketNo { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string BayId { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public int ShelfLifeDays { get; set; }
        public string BestBefore { get; set; } = string.Empty;
        public int DaysLeft { get; set; }
        public double WeightKg { get; set; }
        public double CoconutCount { get; set; }
        public double ApwKg { get; set; }
        public double BalanceKg { get; set; }
        public double BalanceNuts { get; set; }
        public string Condition { get; set; } = string.Empty;   // Fresh / Near expiry / Expired / Consumed
    }

    public sealed class FusionBayItem
    {
        public string TubId { get; set; } = string.Empty;
        public string BayId { get; set; } = string.Empty;
        public double WeightKg { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class FusionTubEvent
    {
        public long Id { get; set; }
        public string TubId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public double WeightKg { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public static class FusionSettingsKeys
    {
        public const string NearExpiryDays = "near_expiry_days";
        public const string ExpiredBelowDays = "expired_below_days";
        public const string TubReplaceAtCycles = "tub_replace_at_cycles";
        public const string TubWarnPercent = "tub_warn_percent";
        public const string ZeroReturn = "zero_return";
    }

    public static class FusionSettingsDefaults
    {
        public const int NearExpiryDays = 7;
        public const int ExpiredBelowDays = 0;
        public const int TubReplaceAtCycles = 2000;
        public const double TubWarnPercent = 80;
        public const bool ZeroReturn = false;
    }

    public static class FusionConditions
    {
        public static string ForStock(double balanceNuts, int daysLeft,
            int nearExpiryDays = FusionSettingsDefaults.NearExpiryDays,
            int expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays)
        {
            if (balanceNuts <= 0) return "Consumed";
            if (daysLeft < expiredBelowDays) return "Expired";
            if (daysLeft <= Math.Max(expiredBelowDays, nearExpiryDays)) return "Near expiry";
            return "Fresh";
        }

        public static string ForPreview(int daysLeft,
            int nearExpiryDays = FusionSettingsDefaults.NearExpiryDays,
            int expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays)
        {
            if (daysLeft < expiredBelowDays) return "Expired";
            if (daysLeft <= Math.Max(expiredBelowDays, nearExpiryDays)) return "Near Expiry (Best Before)";
            return "Fresh";
        }

        public static DateTime BestBefore(string inwardDate, int shelfDays)
        {
            if (!DateTime.TryParse(inwardDate, out var d)) d = DateTime.Today;
            return d.AddDays(shelfDays);
        }
    }
}
