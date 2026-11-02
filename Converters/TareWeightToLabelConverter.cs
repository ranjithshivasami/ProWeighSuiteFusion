using System;
using System.Globalization;
using System.Windows.Data;

namespace ProWeighSuitePlatforms.Converters
{
    public class TareWeightToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Accepts bool (IsTared) - true = "Net Weight", false = "Gross Weight"
            if (value is bool isTared)
                return isTared ? "Net Weight" : "Gross Weight";

            // Legacy fallback: accepts string TareWeight
            if (value is string tareWeight)
            {
                if (string.IsNullOrWhiteSpace(tareWeight) ||
                    double.TryParse(tareWeight, out var weight) && weight == 0)
                    return "Gross Weight";
                return "Net Weight";
            }

            return "Gross Weight";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
