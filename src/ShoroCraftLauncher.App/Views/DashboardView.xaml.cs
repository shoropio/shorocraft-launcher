using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void NewsItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NewsItem item && !string.IsNullOrEmpty(item.Url)
            && Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
