using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSidebarWidth(viewModel.IsSidebarCollapsed);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed) && DataContext is MainViewModel vm)
            UpdateSidebarWidth(vm.IsSidebarCollapsed);
    }

    private void UpdateSidebarWidth(bool collapsed)
    {
        SidebarColumn.Width = new GridLength(collapsed ? 64 : 240);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}
