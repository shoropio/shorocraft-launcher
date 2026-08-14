using System.Windows;

namespace ShoroCraftLauncher.App.Services;

public static class DialogHelper
{
    private static Window? GetOwner()
    {
        try { return Application.Current?.MainWindow; }
        catch { return null; }
    }

    public static System.Windows.MessageBoxResult Show(string message, string caption,
        System.Windows.MessageBoxButton buttons = System.Windows.MessageBoxButton.OK,
        System.Windows.MessageBoxImage icon = System.Windows.MessageBoxImage.None)
    {
        var owner = GetOwner();
        return owner != null
            ? System.Windows.MessageBox.Show(owner, message, caption, buttons, icon)
            : System.Windows.MessageBox.Show(message, caption, buttons, icon);
    }

    public static System.Windows.MessageBoxResult Confirm(string message, string caption = "Confirmar")
        => Show(message, caption, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
}
