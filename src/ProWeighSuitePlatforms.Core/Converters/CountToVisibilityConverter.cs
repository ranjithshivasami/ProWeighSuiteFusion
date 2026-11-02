using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProWeighSuitePlatforms.Converters
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is long l) count = (int)l;
            else if (value is string s && int.TryParse(s, out var p)) count = p;
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
