using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProWeighSuitePlatforms.Converters
{
    /// <summary>
    /// Maps a weighing transaction status string to a dot color.
    /// OK - green, OVER - red, UNDER - orange, NOPRODUCT (or any unrecognized value) - gray.
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        private static readonly Brush _ok = CreateBrush("#4CAF50");
        private static readonly Brush _over = CreateBrush("#F44336");
        private static readonly Brush _under = CreateBrush("#FF9800");
        private static readonly Brush _noProduct = CreateBrush("#9E9E9E");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = (value as string)?.Trim().ToUpperInvariant();

            return status switch
            {
                "OK" => _ok,
                "OVER" => _over,
                "UNDER" => _under,
                "NOPRODUCT" or "NO PRODUCT" => _noProduct,
                _ => _noProduct
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static Brush CreateBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
