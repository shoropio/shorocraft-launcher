using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.App.ViewModels;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ProfilesViewModelTests
{
    private class TestProfileService : IProfileService
    {
        public ObservableCollection<Profile> Profiles { get; } = new();

        private Profile? _selected;
        public Profile? SelectedProfile
        {
            get => _selected;
            set
            {
                _selected = value;
                SelectedProfileChanged?.Invoke();
            }
        }

        public event Action? SelectedProfileChanged;

        public Task CreateBackupAsync(int profileId, string backupType) => Task.CompletedTask;
        public Task DeleteBackupAsync(int profileId, string backupZipPath) => Task.CompletedTask;
        public Task ExportProfileAsync(int profileId, string exportZipPath)
        {
            // create a minimal zip file to simulate export
            using var fs = System.IO.File.Create(exportZipPath);
            var bytes = System.Text.Encoding.UTF8.GetBytes("shorocraft-export");
            fs.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        }
        public Task ImportProfileAsync(string importZipPath) => Task.CompletedTask;
        public Task<List<BackupItem>> GetBackupsAsync(int profileId) => Task.FromResult(new List<BackupItem>());
        public Task LoadProfilesAsync() => Task.CompletedTask;
        public Task RestoreBackupAsync(int profileId, string backupZipPath) => Task.CompletedTask;
        public Task SyncProfileFilesAsync(Profile profile) => Task.CompletedTask;
        public Task UpdateProfileAsync(Profile profile) => Task.CompletedTask;
    }

    [Fact]
    public async Task ExportProfileCommand_CallsDialogAndCreatesFile()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shorocraft-export-tests");
        System.IO.Directory.CreateDirectory(temp);
        var exportPath = System.IO.Path.Combine(temp, "profile-export.zip");

        var profile = new Profile { Id = 1, Name = "VMTest", GameDirectory = temp };

        var profileService = new TestProfileService();
        profileService.Profiles.Add(profile);
        profileService.SelectedProfile = profile;

        var mockRepo = new Mock<IProfileRepository>();
        var mockMinecraft = new Mock<IMinecraftService>();
        var mockLogger = Mock.Of<ILogger<ProfilesViewModel>>();
        var mockLogService = new Mock<Core.Interfaces.ILogService>(MockBehavior.Loose);

        var mockDialog = new Mock<IDialogService>(MockBehavior.Strict);
        mockDialog.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns(exportPath);

        var vm = new ProfilesViewModel(profileService, mockRepo.Object, mockMinecraft.Object, mockLogger, mockLogService.Object, mockDialog.Object);

        // invoke private ExportProfile method via reflection
        var method = typeof(ProfilesViewModel).GetMethod("ExportProfile", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task?)method!.Invoke(vm, null);
        if (task != null) await task;

        Assert.True(System.IO.File.Exists(exportPath));

        try { System.IO.File.Delete(exportPath); } catch { }
    }
}
