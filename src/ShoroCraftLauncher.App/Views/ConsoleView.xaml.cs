using System.Collections.Specialized;
using System.Windows.Threading;
using System.Windows.Controls;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class ConsoleView : UserControl
{
    private ConsoleViewModel? _attachedViewModel;

    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachAutoScroll();
        Unloaded += (_, _) => DetachAutoScroll();
    }

    private void AttachAutoScroll()
    {
        DetachAutoScroll();

        if (DataContext is not ConsoleViewModel vm)
            return;

        _attachedViewModel = vm;
        vm.LogLines.CollectionChanged += LogLinesChanged;
    }

    private void DetachAutoScroll()
    {
        if (_attachedViewModel != null)
            _attachedViewModel.LogLines.CollectionChanged -= LogLinesChanged;

        _attachedViewModel = null;
    }

    private void LogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
        {
            var item = e.NewItems[^1];
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ConsoleList.ScrollIntoView(item)));
        }
    }
}
