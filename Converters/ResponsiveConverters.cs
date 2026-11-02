using System;
using System.Globalization;
using System.Windows.Data;

namespace ProWeighSuitePlatforms.Converters
{
    // Responsive layout helpers, same idea as
    // CSharpDesignPro/WPF---Responsive-UI-Design Converter.cs:
    // bind to ActualWidth and switch templates/columns via DataTrigger.
    // Usage:
    //   <DataTrigger Binding="{Binding ActualWidth, ElementName=Root,
    //               Converter={StaticResource IsLessThanConverter},
    //               ConverterParameter=850}" Value="True"> ...
    public class IsLessThanConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new IsLessThanConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double actual = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double compare = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
                if (double.IsNaN(actual))
                    return false;
                return actual < compare;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsGreaterThanConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new IsGreaterThanConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double actual = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double compare = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
                if (double.IsNaN(actual))
                    return false;
                return actual > compare;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
