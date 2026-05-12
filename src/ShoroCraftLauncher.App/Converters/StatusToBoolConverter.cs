using System.Globalization;
using System.Windows.Data;
using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.App.Converters;

public class StatusToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ModStatus ms) return ms == ModStatus.Active;
        if (value is PackStatus ps) return ps == PackStatus.Active;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isChecked = value is bool b && b;
        if (targetType == typeof(ModStatus)) return isChecked ? ModStatus.Active : ModStatus.Inactive;
        if (targetType == typeof(PackStatus)) return isChecked ? PackStatus.Active : PackStatus.Inactive;
        return false;
    }
}
