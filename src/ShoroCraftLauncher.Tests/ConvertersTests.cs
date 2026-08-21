using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using ShoroCraftLauncher.App.Converters;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ConvertersTests
{
    [Fact]
    public void StatusToBoolConverter_ModStatusActive_ReturnsTrue()
    {
        var converter = new StatusToBoolConverter();
        var result = converter.Convert(ModStatus.Active, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((bool)result!);
    }

    [Fact]
    public void StatusToBoolConverter_ModStatusInactive_ReturnsFalse()
    {
        var converter = new StatusToBoolConverter();
        var result = converter.Convert(ModStatus.Inactive, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.False((bool)result!);
    }

    [Fact]
    public void StatusToBoolConverter_PackStatusActive_ReturnsTrue()
    {
        var converter = new StatusToBoolConverter();
        var result = converter.Convert(PackStatus.Active, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((bool)result!);
    }

    [Fact]
    public void StatusToBoolConverter_UnknownValue_ReturnsFalse()
    {
        var converter = new StatusToBoolConverter();
        var result = converter.Convert("whatever", typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.False((bool)result!);
    }

    [Fact]
    public void StatusToBoolConverter_ConvertBack_BoolToModStatus()
    {
        var converter = new StatusToBoolConverter();
        var active = converter.ConvertBack(true, typeof(ModStatus), null, System.Globalization.CultureInfo.InvariantCulture);
        var inactive = converter.ConvertBack(false, typeof(ModStatus), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(ModStatus.Active, active);
        Assert.Equal(ModStatus.Inactive, inactive);
    }

    [Fact]
    public void StatusToBoolConverter_ConvertBack_BoolToPackStatus()
    {
        var converter = new StatusToBoolConverter();
        var active = converter.ConvertBack(true, typeof(PackStatus), null, System.Globalization.CultureInfo.InvariantCulture);
        var inactive = converter.ConvertBack(false, typeof(PackStatus), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(PackStatus.Active, active);
        Assert.Equal(PackStatus.Inactive, inactive);
    }

    [Theory]
    [InlineData("[Error] algo falló", 239, 68, 68)]
    [InlineData("[Warning] cuidado", 245, 158, 11)]
    [InlineData("[Info] todo bien", 241, 245, 249)]
    [InlineData("[Debug] detalle", 100, 116, 139)]
    [InlineData("sin etiqueta", 241, 245, 249)]
    public void LogLevelColorConverter_MapsLevelsToColors(string line, byte r, byte g, byte b)
    {
        var converter = new LogLevelColorConverter();
        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(line, typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Color.FromRgb(r, g, b), brush.Color);
    }

    [Fact]
    public void LogLevelColorConverter_NullOrEmpty_ReturnsDefaultBrush()
    {
        var converter = new LogLevelColorConverter();
        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(null!, typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Color.FromRgb(241, 245, 249), brush.Color);
    }

    private static (Mod Mod, ObservableCollection<ModUpdateInfo> Updates) CreateModWithUpdate(int modId)
    {
        var mod = new Mod { Id = modId, Name = "JEI" };
        var updates = new ObservableCollection<ModUpdateInfo>
        {
            new(modId, "JEI", "1.0.0", "2.0.0", "Modrinth", "remote-1", "jei")
        };
        return (mod, updates);
    }

    [Fact]
    public void ModUpdateHasUpdateConverter_WithMatchingUpdate_ReturnsTrue()
    {
        var (mod, updates) = CreateModWithUpdate(7);
        var converter = new ModUpdateHasUpdateConverter();

        var result = converter.Convert(mod, typeof(bool), updates, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True((bool)result!);
    }

    [Fact]
    public void ModUpdateHasUpdateConverter_WithoutMatchingUpdate_ReturnsFalse()
    {
        var (mod, updates) = CreateModWithUpdate(7);
        var otherUpdates = new ObservableCollection<ModUpdateInfo>();
        foreach (var u in updates) otherUpdates.Add(new ModUpdateInfo(99, u.ModName, u.CurrentVersion, u.LatestVersion, u.Provider, u.RemoteProjectId, u.RemoteSlug));
        var converter = new ModUpdateHasUpdateConverter();

        var result = converter.Convert(mod, typeof(bool), otherUpdates, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False((bool)result!);
    }

    [Fact]
    public void ModUpdateHasUpdateConverter_WrongTypes_ReturnsFalse()
    {
        var converter = new ModUpdateHasUpdateConverter();
        var result = converter.Convert("no-mod", typeof(bool), "no-updates", System.Globalization.CultureInfo.InvariantCulture);
        Assert.False((bool)result!);
    }

    [Fact]
    public void ModUpdateLatestVersionConverter_WithMatchingUpdate_ReturnsLatestVersion()
    {
        var (mod, updates) = CreateModWithUpdate(7);
        var converter = new ModUpdateLatestVersionConverter();

        var result = converter.Convert(mod, typeof(string), updates, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public void ModUpdateLatestVersionConverter_WithoutMatch_ReturnsEmptyString()
    {
        var (mod, _) = CreateModWithUpdate(7);
        var converter = new ModUpdateLatestVersionConverter();

        var result = converter.Convert(mod, typeof(string), new ObservableCollection<ModUpdateInfo>(), System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ImageSourceConverter_NullOrEmptyPath_ReturnsNull()
    {
        var converter = new ShoroCraftLauncher.App.Converters.ImageSourceConverter();
        Assert.Null(converter.Convert(null!, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(string.Empty, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ImageSourceConverter_NonExistentFile_ReturnsNull()
    {
        var converter = new ShoroCraftLauncher.App.Converters.ImageSourceConverter();
        var result = converter.Convert(@"Z:\definitivamente\inexistente\icono.png", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Null(result);
    }
}
