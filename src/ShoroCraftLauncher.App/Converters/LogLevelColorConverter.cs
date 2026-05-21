using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ShoroCraftLauncher.App.Converters;

public class LogLevelColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var line = value as string;
        if (string.IsNullOrEmpty(line)) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));

        if (line.Contains("[Error]", StringComparison.OrdinalIgnoreCase) || line.Contains("[Critical]", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // ErrorColor
        if (line.Contains("[Warning]", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // WarningColor
        if (line.Contains("[Info]", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249)); // TextPrimary
        if (line.Contains("[Debug]", StringComparison.OrdinalIgnoreCase) || line.Contains("[Trace]", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)); // TextMuted

        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
