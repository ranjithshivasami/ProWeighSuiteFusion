using System;
using System.Globalization;
using System.Windows.Data;

namespace ProWeighSuitePlatforms.Converters
{
    public class DateOnlyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            }

            var nullable = value as DateTime?;
            if (nullable.HasValue)
            {
                return nullable.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && DateTime.TryParseExact(text, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }

            return Binding.DoNothing;
        }
    }
}
