using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class MainWindow : Window
{
    private bool _isAnimating;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SidebarColumn.Width = new GridLength(viewModel.IsSidebarCollapsed ? 64 : 240);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed) && DataContext is MainViewModel vm)
            AnimateSidebarWidth(vm.IsSidebarCollapsed);
    }

    private void AnimateSidebarWidth(bool collapsed)
    {
        if (_isAnimating) return;

        var targetWidth = collapsed ? 64.0 : 240.0;
        var currentWidth = SidebarBorder.ActualWidth > 0 ? SidebarBorder.ActualWidth : (collapsed ? 64.0 : 240.0);

        if (System.Math.Abs(currentWidth - targetWidth) < 1) return;

        _isAnimating = true;

        var animation = new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) =>
        {
            SidebarColumn.Width = new GridLength(targetWidth);
            _isAnimating = false;
        };

        SidebarColumn.Width = new GridLength(currentWidth);
        SidebarBorder.Width = currentWidth;
        SidebarBorder.BeginAnimation(WidthProperty, animation);
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
