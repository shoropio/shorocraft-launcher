using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.App.Services;

public class DialogService : IDialogService
{
    public string[]? ShowOpenFileDialog(string filter, string title, bool multiselect = false)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Title = title,
            Multiselect = multiselect
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileNames;
        }

        return null;
    }

    public string? ShowFolderBrowserDialog(string description)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = description;
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            return dialog.SelectedPath;
        }

        return null;
    }

    public string? ShowSaveFileDialog(string filter, string title, string? defaultFileName = null)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName ?? string.Empty
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }

        return null;
    }
}
