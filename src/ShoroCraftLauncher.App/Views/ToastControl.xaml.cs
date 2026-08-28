using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShoroCraftLauncher.App.Models;

namespace ShoroCraftLauncher.App.Views;

public partial class ToastControl : UserControl
{
    private readonly Dictionary<ToastSeverity, string> _accentColors = new()
    {
        [ToastSeverity.Info] = "#4caf50",
        [ToastSeverity.Success] = "#4caf50",
        [ToastSeverity.Warning] = "#ff9800",
        [ToastSeverity.Error] = "#f44336"
    };

    public ToastControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToastItem item) return;

        if (_accentColors.TryGetValue(item.Severity, out var color))
            Accent.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        var sb = new Storyboard();
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)));
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        var slide = new DoubleAnimation(-40, 0, new Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, Tf);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty));
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();

        if (item.Duration is { TotalMilliseconds: > 0 } duration)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = duration
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                item.DismissCommand.Execute(null);
            };
            timer.Start();
        }
    }
}
