using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProWeighSuitePlatforms.Converters
{
    public class AlternatingRowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Simple alternating background - every other row
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
