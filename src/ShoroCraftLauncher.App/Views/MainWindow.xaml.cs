using System.Windows;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}
