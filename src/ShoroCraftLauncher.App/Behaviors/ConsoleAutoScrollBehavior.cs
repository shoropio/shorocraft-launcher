using System.Windows;
using System.Windows.Controls;

namespace ShoroCraftLauncher.App.Behaviors
{
    public static class ConsoleAutoScrollBehavior
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(ConsoleAutoScrollBehavior),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb)
                return;

            if ((bool)e.NewValue)
                tb.TextChanged += OnTextChanged;
            else
                tb.TextChanged -= OnTextChanged;
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;

            var host = tb.Template?.FindName("PART_ContentHost", tb) as ScrollViewer;
            if (host != null)
            {
                double extent = host.ExtentHeight;
                double viewport = host.ViewportHeight;
                double offset = host.VerticalOffset;
                bool nearBottom = offset >= extent - viewport - 12;
                if (!nearBottom)
                    return;
            }

            tb.ScrollToEnd();
        }
    }
}
