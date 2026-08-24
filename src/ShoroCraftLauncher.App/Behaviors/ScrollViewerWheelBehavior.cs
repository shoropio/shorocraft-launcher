using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShoroCraftLauncher.App.Behaviors
{
    public static class ScrollViewerWheelBehavior
    {
        public static readonly DependencyProperty BubbleWheelProperty =
            DependencyProperty.RegisterAttached(
                "BubbleWheel",
                typeof(bool),
                typeof(ScrollViewerWheelBehavior),
                new PropertyMetadata(false, OnBubbleWheelChanged));

        public static bool GetBubbleWheel(DependencyObject obj) => (bool)obj.GetValue(BubbleWheelProperty);

        public static void SetBubbleWheel(DependencyObject obj, bool value) => obj.SetValue(BubbleWheelProperty, value);

        private static void OnBubbleWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer sv)
                return;

            if ((bool)e.NewValue)
                sv.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                sv.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;

            double extent = sv.ExtentHeight;
            double viewport = sv.ViewportHeight;
            if (extent <= viewport + 0.5)
                return;

            double offset = sv.VerticalOffset;
            bool scrollingUp = e.Delta > 0;
            bool atTop = offset <= 0.5;
            bool atBottom = offset >= extent - viewport - 0.5;

            if ((scrollingUp && atTop) || (!scrollingUp && atBottom))
                return;

            sv.ScrollToVerticalOffset(offset - e.Delta);
            e.Handled = true;
        }
    }
}
