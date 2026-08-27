using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class ServersView : UserControl
{
    private bool _scrollAttached;

    public ServersView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachLogScroll();
    }

    private void AttachLogScroll()
    {
        if (DataContext is not ServersViewModel vm) return;
        if (_scrollAttached) return;
        _scrollAttached = true;
        vm.LogLines.CollectionChanged += LogLines_CollectionChanged;
        if (vm.LogLines.Count > 0)
            Dispatcher.BeginInvoke(ScrollToBottom);
    }

    private void LogLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || e.NewItems.Count == 0)
            return;

        Dispatcher.BeginInvoke(ScrollToBottom);
    }

    private void ScrollToBottom()
    {
        try
        {
            if (VisualTreeHelper.GetChildrenCount(ServerConsole) == 0)
                return;

            if (VisualTreeHelper.GetChild(ServerConsole, 0) is Border border && border.Child is ScrollViewer sv)
            {
                sv.ScrollToBottom();
                return;
            }
        }
        catch
        {
            // La plantilla del ListBox todavia no se ha materializado; se reintentara en el proximo evento.
        }

        if (ServerConsole.Items.Count > 0)
        {
            try
            {
                ServerConsole.ScrollIntoView(ServerConsole.Items[^1]);
            }
            catch
            {
            }
        }
    }

    private void CommandBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (DataContext is ServersViewModel vm && vm.SendCommandCommand.CanExecute(null))
            vm.SendCommandCommand.Execute(null);
    }
}
