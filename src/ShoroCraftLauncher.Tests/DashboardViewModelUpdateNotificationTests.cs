using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.App.ViewModels;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class DashboardViewModelUpdateNotificationTests
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
        public Task ExportProfileAsync(int profileId, string exportZipPath) => Task.CompletedTask;
        public Task ImportProfileAsync(string importZipPath) => Task.CompletedTask;
        public Task<List<BackupItem>> GetBackupsAsync(int profileId) => Task.FromResult(new List<BackupItem>());
        public Task LoadProfilesAsync() => Task.CompletedTask;
        public Task RestoreBackupAsync(int profileId, string backupZipPath) => Task.CompletedTask;
        public Task SyncProfileFilesAsync(Profile profile) => Task.CompletedTask;
        public Task UpdateProfileAsync(Profile profile) => Task.CompletedTask;
    }

    private static DashboardViewModel CreateViewModel(
        TestProfileService profileService,
        ISettingsRepository settingsRepo)
    {
        return new DashboardViewModel(
            profileService,
            Mock.Of<IGameVersionRepository>(),
            Mock.Of<IMinecraftService>(),
            Mock.Of<ILauncherService>(),
            Mock.Of<IJavaService>(),
            Mock.Of<IUpdaterService>(),
            Mock.Of<IModService>(),
            settingsRepo,
            Mock.Of<ILogger<DashboardViewModel>>());
    }

    private static Task InvokeCheckNotificationAsync(DashboardViewModel vm, string latest)
    {
        var method = typeof(DashboardViewModel).GetMethod("CheckMinecraftUpdateNotificationAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task?)method!.Invoke(vm, new object?[] { latest });
        Assert.NotNull(task);
        return task!;
    }

    [Fact]
    public async Task CheckNotification_NoLastNotified_ShowsNotification()
    {
        var profileService = new TestProfileService();
        profileService.Profiles.Add(new Profile
        {
            Id = 1,
            Name = "Test",
            Type = ProfileType.Vanilla,
            MinecraftVersion = "1.20.1"
        });

        var settingsRepo = new Mock<ISettingsRepository>(MockBehavior.Strict);
        settingsRepo.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        var vm = CreateViewModel(profileService, settingsRepo.Object);
        await InvokeCheckNotificationAsync(vm, "1.21.0");

        Assert.True(vm.HasUpdateNotification);
        Assert.Contains("1.21.0", vm.UpdateNotificationMessage);
    }

    [Fact]
    public async Task CheckNotification_SameVersionNotified_DoesNotShowAgain()
    {
        var profileService = new TestProfileService();
        profileService.Profiles.Add(new Profile
        {
            Id = 1,
            Name = "Test",
            Type = ProfileType.Vanilla,
            MinecraftVersion = "1.20.1"
        });

        var settingsRepo = new Mock<ISettingsRepository>(MockBehavior.Strict);
        settingsRepo.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync("1.21.0");

        var vm = CreateViewModel(profileService, settingsRepo.Object);
        await InvokeCheckNotificationAsync(vm, "1.21.0");

        Assert.False(vm.HasUpdateNotification);
    }

    [Fact]
    public async Task CheckNotification_NewerVersionAfterDismiss_ShowsNotification()
    {
        var profileService = new TestProfileService();
        profileService.Profiles.Add(new Profile
        {
            Id = 1,
            Name = "Test",
            Type = ProfileType.Vanilla,
            MinecraftVersion = "1.20.1"
        });

        var settingsRepo = new Mock<ISettingsRepository>(MockBehavior.Strict);
        settingsRepo.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync("1.21.0");

        var vm = CreateViewModel(profileService, settingsRepo.Object);
        await InvokeCheckNotificationAsync(vm, "1.21.4");

        Assert.True(vm.HasUpdateNotification);
        Assert.Contains("1.21.4", vm.UpdateNotificationMessage);
    }

    [Fact]
    public async Task DismissUpdate_PersistsLastNotifiedVersion()
    {
        var profileService = new TestProfileService();
        profileService.Profiles.Add(new Profile
        {
            Id = 1,
            Name = "Test",
            Type = ProfileType.Vanilla,
            MinecraftVersion = "1.20.1"
        });

        var settingsRepo = new Mock<ISettingsRepository>(MockBehavior.Strict);
        settingsRepo.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        settingsRepo.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = CreateViewModel(profileService, settingsRepo.Object);
        await InvokeCheckNotificationAsync(vm, "1.21.0");
        Assert.True(vm.HasUpdateNotification);

        var dismissMethod = typeof(DashboardViewModel).GetMethod("DismissMinecraftUpdateAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(dismissMethod);
        var dismissTask = (Task?)dismissMethod!.Invoke(vm, null);
        if (dismissTask != null) await dismissTask;

        Assert.False(vm.HasUpdateNotification);
        settingsRepo.Verify(r => r.SetAsync(It.IsAny<string>(), "1.21.0"), Times.Once);
    }
}
