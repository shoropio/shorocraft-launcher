using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Models;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;


public partial class DashboardViewModel : BaseViewModel, IDisposable
{
    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        ReadyStatus = "Obteniendo versiones...";
        LogStatus("Obteniendo versiones estables de Minecraft...");
        try
        {
            var versions = await _minecraftService.FetchAvailableVersionsAsync();
            var stableVersions = versions
                .Where(v => v.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();

            MarkInstalledVersions(stableVersions);

            AvailableVersions.Clear();
            foreach (var v in stableVersions)
                AvailableVersions.Add(v);

            if (stableVersions.Count > 0 && (string.IsNullOrWhiteSpace(SelectedVersion) || SelectedVersion == "latest"))
            {
                var installed = stableVersions.FirstOrDefault(v => v.IsInstalled);
                SelectedVersion = installed?.VersionId ?? stableVersions[0].VersionId;
            }

            if (stableVersions.Count > 0)
            {
                var latest = stableVersions[0].VersionId;
                _latestAvailableVersion = latest;
                if (SelectedProfile != null && SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
                {
                    if (SelectedProfile.MinecraftVersion != "latest" && SelectedProfile.MinecraftVersion != latest)
                    {
                        await CheckMinecraftUpdateNotificationAsync(latest);
                    }
                    else
                    {
                        HasUpdateNotification = false;
                    }
                }
                else
                {
                    HasUpdateNotification = false;
                }
            }

            if (stableVersions.Count == 0)
            {
                ReadyStatus = "Sin datos";
                StatusMessage = "No se pudieron obtener versiones estables.";
                _launcherService.Log($"[WARN] {StatusMessage}");
                return;
            }

            var installedStable = stableVersions.FirstOrDefault(v => v.IsInstalled);
            ReadyStatus = installedStable != null ? "Instalado" : "Listo";
            StatusMessage = installedStable != null
                ? $"Versión estable instalada: {installedStable.VersionId}."
                : $"Lista actualizada. Ultima estable: {stableVersions[0].VersionId}.";
            _launcherService.Log($"[INFO] {StatusMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch versions");
            ReadyStatus = "Error";
            StatusMessage = "Error al obtener versiones.";
            _launcherService.Log($"[ERROR] Error al obtener versiones: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckMinecraftUpdateNotificationAsync(string latest)
    {
        try
        {
            _latestAvailableVersion = latest;
            var lastNotified = await _settingsRepo.GetAsync(LastNotifiedVersionKey);
            if (!string.IsNullOrEmpty(lastNotified) && IsVersionAtLeast(lastNotified, latest))
            {
                HasUpdateNotification = false;
                return;
            }

            HasUpdateNotification = true;
            UpdateNotificationMessage = $"¡La nueva versión de Minecraft {latest} ya está disponible!";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Minecraft update notification");
            HasUpdateNotification = false;
        }
    }

    private static bool IsVersionAtLeast(string candidate, string baseline)
    {
        var candidateParts = candidate.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
        var baselineParts = baseline.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();

        var length = Math.Max(candidateParts.Length, baselineParts.Length);
        for (int i = 0; i < length; i++)
        {
            var a = i < candidateParts.Length ? candidateParts[i] : 0;
            var b = i < baselineParts.Length ? baselineParts[i] : 0;
            if (a != b)
                return a > b;
        }
        return true;
    }

    private async Task InstallMinecraftUpdateAsync()
    {
        if (string.IsNullOrEmpty(_latestAvailableVersion))
        {
            HasUpdateNotification = false;
            return;
        }

        HasUpdateNotification = false;
        await InstallVersion(_latestAvailableVersion);
    }

    private async Task DismissMinecraftUpdateAsync()
    {
        HasUpdateNotification = false;
        if (!string.IsNullOrEmpty(_latestAvailableVersion))
        {
            try
            {
                await _settingsRepo.SetAsync(LastNotifiedVersionKey, _latestAvailableVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist dismissed Minecraft version");
            }
        }
    }

    private void MarkInstalledVersions(List<GameVersion> versions)
    {
        if (SelectedProfile == null)
            return;

        var gameDir = GetSelectedProfileGameDirectory();
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
            return;

        var installedNames = Directory.GetDirectories(versionsDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            version.IsInstalled = installedNames.Contains(version.VersionId)
                || installedNames.Any(installed =>
                    installed!.Contains(version.VersionId, StringComparison.OrdinalIgnoreCase)
                    && (installed.Contains("fabric", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("forge", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("quilt", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("optifine", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private async Task InstallVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return;
        if (IsDownloading || IsBusy) return;
        IsDownloading = true;
        DownloadProgress = 0;
        ReadyStatus = $"Instalando {versionId}...";
        StatusMessage = $"Instalando Minecraft {versionId}...";

        try
        {
            if (versionId.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versión estable más nueva de Minecraft...");
                versionId = await _minecraftService.ResolveVersionIdAsync("latest");
                ReadyStatus = $"Instalando {versionId}...";
            }

            LogStatus($"Instalando Minecraft {versionId}...");
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _minecraftService.InstallVersionAsync(versionId, progress, GetSelectedProfileGameDirectory());
            InstalledVersion = versionId;
            var installed = AvailableVersions.FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
            if (installed != null)
                installed.IsInstalled = true;
            ReadyStatus = "Listo";
            LogStatus($"Minecraft {versionId} instalado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install failed");
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando Minecraft {versionId}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

}
