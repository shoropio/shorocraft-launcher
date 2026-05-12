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
}
