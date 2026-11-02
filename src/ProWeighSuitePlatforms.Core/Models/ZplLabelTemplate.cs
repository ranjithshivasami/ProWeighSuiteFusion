using System;
using System.Text.Json.Serialization;

namespace ProWeighSuitePlatforms.Models
{
    public class ZplLabelTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Raw ZPL code containing {{Placeholder}} tokens that are resolved at print time.
        /// </summary>
        public string ZplCode { get; set; } = string.Empty;

        /// <summary>
        /// Label width in millimetres (e.g. 100 for a 100 mm label).
        /// </summary>
        public int LabelWidthMm { get; set; } = 100;

        /// <summary>
        /// Label height in millimetres (e.g. 50 for a 50 mm label).
        /// </summary>
        public int LabelHeightMm { get; set; } = 50;

        /// <summary>
        /// Printer resolution in DPI (dots per inch). Common values: 152, 203, 300, 600.
        /// </summary>
        public int Dpi { get; set; } = 203;

        /// <summary>
        /// Dots per millimetre derived from DPI (used by Labelary API: 6, 8, 12, 24 dpmm).
        /// </summary>
        [JsonIgnore]
        public int Dpmm => Dpi switch
        {
            152 => 6,
            203 => 8,
            300 => 12,
            600 => 24,
            _ => 8
        };

        /// <summary>
        /// Label width in dots = WidthMm * Dpmm.
        /// </summary>
        [JsonIgnore]
        public int LabelWidthDots => LabelWidthMm * Dpmm;

        /// <summary>
        /// Label height in dots = HeightMm * Dpmm.
        /// </summary>
        [JsonIgnore]
        public int LabelHeightDots => LabelHeightMm * Dpmm;

        /// <summary>
        /// Only one template can be active at a time for printing.
        /// </summary>
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ModifiedAt { get; set; }

        // Display helpers
        [JsonIgnore]
        public string ActiveStatusText => IsActive ? "Active" : "Inactive";

        [JsonIgnore]
        public string LabelSizeText => $"{LabelWidthMm} � {LabelHeightMm} mm @ {Dpi} DPI";

        [JsonIgnore]
        public int SerialNumber { get; set; }
    }
}
