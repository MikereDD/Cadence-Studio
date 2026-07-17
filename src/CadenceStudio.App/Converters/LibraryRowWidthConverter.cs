using System;
using System.Globalization;
using System.Windows.Data;

namespace CadenceStudio.App.Converters;

public sealed class LibraryRowWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || double.IsInfinity(width))
        {
            return 0d;
        }

        var reserved = 34d;
        if (parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            reserved = parsed;
        }

        return Math.Max(0d, width - reserved);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
