namespace ShoroCraftLauncher.Core.Interfaces;

public interface IDialogService
{
    string[]? ShowOpenFileDialog(string filter, string title, bool multiselect = false);
    string? ShowFolderBrowserDialog(string description);
}
