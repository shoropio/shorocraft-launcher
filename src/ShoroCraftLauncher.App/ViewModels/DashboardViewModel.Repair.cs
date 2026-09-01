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
    private async Task RepairProfile()
    {
        if (SelectedProfile == null) return;
        if (IsBusy || IsDownloading) return;
        IsBusy = true;
        ReadyStatus = "Reparando perfil...";
        LogStatus("Validando y reparando archivos clave...");

        try
        {
            var gameDir = GetSelectedProfileGameDirectory();

            _launcherService.Log("[INFO] Asegurando jerarquía de carpetas...");
            await _profileService.SyncProfileFilesAsync(SelectedProfile);
            await _minecraftService.RepairInstallationAsync(gameDir);

            var targetVersion = SelectedProfile.MinecraftVersion;
            if (targetVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }
            
            var mcPath = new CmlLib.Core.MinecraftPath(gameDir);
            var verPath = Path.Combine(mcPath.Versions, targetVersion);
            if (!Directory.Exists(verPath) || !File.Exists(Path.Combine(verPath, $"{targetVersion}.json")))
            {
                _launcherService.Log("[INFO] Descargando archivos de Minecraft faltantes...");
                var progress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallVersionAsync(targetVersion, progress, gameDir);
            }

            if (SelectedProfile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
            {
                await ValidateProfileChecklistAsync();
                if (!IsLoaderReady)
                {
                    var loaderType = SelectedProfile.Type.ToString();
                    var loaderVer = SelectedProfile.LoaderVersion;
                    if (string.IsNullOrEmpty(loaderVer) || loaderVer.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        loaderVer = await _minecraftService.ResolveLatestLoaderVersionAsync(loaderType, targetVersion);
                    }

                    var javaPath = SelectedProfile.JavaPath;
                    if (string.IsNullOrEmpty(javaPath))
                    {
                        javaPath = await _javaService.GetRecommendedJavaPathAsync(targetVersion);
                    }

                    if (!string.IsNullOrEmpty(javaPath) && File.Exists(javaPath))
                    {
                        _launcherService.Log($"[INFO] Reinstalando cargador {loaderType} {loaderVer}...");
                        var progress = new Progress<double>(p => DownloadProgress = p);
                        await _minecraftService.InstallLoaderAsync(targetVersion, loaderType, loaderVer, javaPath, _ => {}, progress, _launcherService.Log, gameDir);
                    }
                }
            }

            LogStatus("Reparación finalizada.");
            ReadyStatus = "Listo";
            await UpdateProfileDetailsAsync();
            await UpdateComponentInstallStatesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair profile failed");
            ReadyStatus = "Error";
            StatusMessage = $"Error al reparar: {ex.Message}";
            _launcherService.Log($"[ERROR] Error al reparar perfil: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
