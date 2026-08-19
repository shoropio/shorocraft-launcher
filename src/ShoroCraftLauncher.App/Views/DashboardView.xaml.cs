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
        if (sender is FrameworkElement fe && fe.DataContext is NewsItem item && !string.IsNullOrEmpty(item.Url))
        {
            Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        }
    }
}
