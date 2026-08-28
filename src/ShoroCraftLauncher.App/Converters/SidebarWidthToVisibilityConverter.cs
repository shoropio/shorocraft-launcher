using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShoroCraftLauncher.App.Converters;

public class SidebarWidthToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var threshold = 200.0;
        if (parameter?.ToString() is { } p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var t))
            threshold = t;

        var width = value is double d ? d : 0;
        return width >= threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
