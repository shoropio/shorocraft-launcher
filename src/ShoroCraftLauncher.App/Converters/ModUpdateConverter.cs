using System.Globalization;
using System.Windows.Data;
using System.Collections.ObjectModel;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.Converters;

public class ModUpdateHasUpdateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Mod mod || parameter is not ObservableCollection<ModUpdateInfo> updates) return false;

        var existingUpdate = updates.FirstOrDefault(u => u.ModId == mod.Id);
        return existingUpdate != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ModUpdateLatestVersionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Mod mod || parameter is not ObservableCollection<ModUpdateInfo> updates) return "";

        var existingUpdate = updates.FirstOrDefault(u => u.ModId == mod.Id);
        return existingUpdate?.LatestVersion ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}