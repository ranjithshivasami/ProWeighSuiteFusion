// Option B/B1 — pure weighing evaluation, extracted verbatim from
// BatchWeighingViewModel (UpdateToleranceLimits, GetWeightResultStatus,
// MapStatusForDatabase, CanRecordStatus, ShouldPrintForStatus, deviation).
// No WPF, no threading, no services — unit-testable in isolation.
using System;

namespace ProWeighSuitePlatforms.Evaluation
{
    public static class WeighingEvaluation
    {
        public static (double Lower, double Upper) ToleranceBands(
            string? toleranceMode, double targetWeight,
            double tMinusPercent, double tPlusPercent,
            double minWeight, double maxWeight)
        {
            if (string.Equals(toleranceMode, "Absolute", StringComparison.OrdinalIgnoreCase))
                return (minWeight, maxWeight);
            if (targetWeight > 0)
                return (targetWeight * (1 - tMinusPercent / 100.0),
                        targetWeight * (1 + tPlusPercent / 100.0));
            return (0.0, 0.0);
        }

        public static string ResultStatus(
            double netWeight, double presenceThreshold, double lowerWeight, double upperWeight)
        {
            if (netWeight < presenceThreshold) return "No Product";
            if (netWeight < lowerWeight) return "Under Weight";
            if (netWeight > upperWeight) return "Over Weight";
            return "Accepted";
        }

        public static string MapStatusForDatabase(string displayStatus) => displayStatus switch
        {
            "Accepted" => "OK",
            "Over Weight" => "OVER",
            "Under Weight" => "UNDER",
            "No Product" => "NOPRODUCT",
            _ => displayStatus
        };

        public static bool CanRecord(string status, bool atAccept, bool atUnder, bool atOver) => status switch
        {
            "Accepted" => atAccept,
            "Under Weight" => atUnder,
            "Over Weight" => atOver,
            _ => false
        };

        public static bool ShouldPrint(string status, bool atAccept, bool atUnder, bool atOver) => status switch
        {
            "Accepted" => atAccept,
            "Under Weight" => atUnder,
            "Over Weight" => atOver,
            _ => false
        };

        public static double Deviation(double targetWeight, double netWeight)
            => targetWeight > 0 ? ((netWeight - targetWeight) / targetWeight) * 100.0 : 0.0;

        /// <summary>
        /// When-policy for auto-capture (was three inline
        /// `_isAutoCaptureEnabled &amp;&amp; IsStarted &amp;&amp; RecordAtX` checks in UpdateGaugeAndStatus).
        /// </summary>
        public static bool ShouldAutoCapture(string status, bool autoEnabled, bool started,
            bool recAccept, bool recUnder, bool recOver)
        {
            if (!autoEnabled || !started) return false;
            return status switch
            {
                "Accepted" => recAccept,
                "Under Weight" => recUnder,
                "Over Weight" => recOver,
                _ => false
            };
        }
    }
}
