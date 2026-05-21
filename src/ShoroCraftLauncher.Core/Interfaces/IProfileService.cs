using System.Collections.ObjectModel;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IProfileService
{
    Profile? SelectedProfile { get; set; }
    ObservableCollection<Profile> Profiles { get; }
    event Action? SelectedProfileChanged;
    Task LoadProfilesAsync();
    Task UpdateProfileAsync(Profile profile);
    Task SyncProfileFilesAsync(Profile profile);
    Task ExportProfileAsync(int profileId, string exportZipPath);
    Task ImportProfileAsync(string importZipPath);
    Task CreateBackupAsync(int profileId, string backupType);
    Task RestoreBackupAsync(int profileId, string backupZipPath);
    Task DeleteBackupAsync(int profileId, string backupZipPath);
    Task<List<BackupItem>> GetBackupsAsync(int profileId);
}
