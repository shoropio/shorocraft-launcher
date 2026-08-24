namespace ShoroCraftLauncher.Core.Interfaces;

public interface IControllerDetectionService
{
    Task<bool> IsAnyControllerConnectedAsync();
}
