using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NetSentry.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ThresholdToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush RedBrush = new(System.Windows.Media.Color.FromRgb(255, 71, 87));
        private static readonly SolidColorBrush GreenBrush = new(System.Windows.Media.Color.FromRgb(46, 213, 115));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBlocked && isBlocked)
                return RedBrush;
            return GreenBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusBgBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush DarkRedBg = new(System.Windows.Media.Color.FromRgb(59, 28, 36));
        private static readonly SolidColorBrush DarkGreenBg = new(System.Windows.Media.Color.FromRgb(28, 56, 41));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBlocked && isBlocked)
                return DarkRedBg;
            return DarkGreenBg;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
