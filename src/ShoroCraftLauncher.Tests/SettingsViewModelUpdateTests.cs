using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.App.ViewModels;
using ShoroCraftLauncher.Core.Interfaces;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class SettingsViewModelUpdateTests
{
    [Fact]
    public async Task CheckUpdates_NoUpdateAvailable_SetsNoUpdateMessage()
    {
        var settingsRepo = new Mock<ISettingsRepository>(MockBehavior.Loose);
        var logService = new Mock<ILogService>(MockBehavior.Loose);
        var updaterService = new Mock<IUpdaterService>(MockBehavior.Strict);
        updaterService
            .Setup(u => u.CheckForUpdatesAsync(It.IsAny<string>()))
            .ReturnsAsync((false, (string?)null, (string?)null, (string?)null));

        var vm = new SettingsViewModel(
            settingsRepo.Object,
            Mock.Of<ILogger<SettingsViewModel>>(),
            logService.Object,
            updaterService.Object);

        var method = typeof(SettingsViewModel).GetMethod("CheckUpdates",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task?)method!.Invoke(vm, null);
        if (task != null) await task;

        Assert.Equal("No hay actualizaciones disponibles.", vm.StatusMessage);
    }
}
