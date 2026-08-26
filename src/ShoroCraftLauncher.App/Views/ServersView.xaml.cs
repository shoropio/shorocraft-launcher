using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class ServersView : UserControl
{
    public ServersView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachLogScroll();
    }

    private void AttachLogScroll()
    {
        if (DataContext is not ServersViewModel vm) return;
        vm.LogLines.CollectionChanged += LogLines_CollectionChanged;
        if (vm.LogLines.Count > 0)
            ScrollToBottom();
    }

    private void LogLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || e.NewItems.Count == 0)
            return;

        Dispatcher.BeginInvoke(ScrollToBottom);
    }

    private void ScrollToBottom()
    {
        if (VisualTreeHelper.GetChild(ServerConsole, 0) is Border border && border.Child is ScrollViewer sv)
        {
            sv.ScrollToBottom();
            return;
        }

        if (ServerConsole.Items.Count > 0)
            ServerConsole.ScrollIntoView(ServerConsole.Items[^1]);
    }

    private void CommandBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (DataContext is ServersViewModel vm && vm.SendCommandCommand.CanExecute(null))
            vm.SendCommandCommand.Execute(null);
    }
}
