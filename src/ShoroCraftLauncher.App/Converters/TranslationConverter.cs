using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShoroCraftLauncher.App.Converters;

public class TranslationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current.Resources.Contains(key))
            return Application.Current.Resources[key] ?? value;
            
        return value ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
