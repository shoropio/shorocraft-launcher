using System.Windows.Input;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class ServersView : UserControl
{
    public ServersView()
    {
        InitializeComponent();
    }

    private void CommandBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (DataContext is ServersViewModel vm && vm.SendCommandCommand.CanExecute(null))
            vm.SendCommandCommand.Execute(null);
    }
}
