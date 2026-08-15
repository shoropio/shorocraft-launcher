using System.Windows.Controls;
using ShoroCraftLauncher.App.ViewModels;

namespace ShoroCraftLauncher.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _suppressUiUpdate;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void AttachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.CurseForgeApiKeyChanged -= OnCurseForgeApiKeyChanged;
            _viewModel = null;
        }

        if (DataContext is SettingsViewModel vm)
        {
            _viewModel = vm;
            _viewModel.CurseForgeApiKeyChanged += OnCurseForgeApiKeyChanged;
            OnCurseForgeApiKeyChanged(this, vm.CurseForgeApiKey);
        }
    }

    private void OnCurseForgeApiKeyChanged(object? sender, string value)
    {
        _suppressUiUpdate = true;
        try { CurseForgeKeyBox.Password = value ?? string.Empty; }
        finally { _suppressUiUpdate = false; }
    }

    private void CurseForgeKeyBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_suppressUiUpdate) return;
        _viewModel?.SetCurseForgeApiKeyFromUi(CurseForgeKeyBox.Password);
    }

    private void RevealKeyButton_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        CurseForgeKeyBox.PasswordChar = RevealKeyButton.IsChecked == true ? '\0' : '●';
    }
}
