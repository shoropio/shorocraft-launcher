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
    private async Task UpdateComponentInstallStatesAsync()
    {
        if (SelectedProfile == null)
        {
            IsIrisSodiumInstalled = false;
            return;
        }

        try
        {
            var knownNames = new List<string>();
            var mods = await _modService.GetModsAsync(SelectedProfile.Id);
            knownNames.AddRange(mods
                .Where(m => m.Status == ShoroCraftLauncher.Core.Enums.ModStatus.Active)
                .SelectMany(m => new[] { m.Name, m.FileName, m.ModVersion }));

            var modsDir = _minecraftService.GetModsDirectory(GetSelectedProfileGameDirectory());
            if (Directory.Exists(modsDir))
            {
                knownNames.AddRange(Directory.GetFiles(modsDir, "*.jar")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!));
            }

            IsIrisSodiumInstalled = ContainsComponent(knownNames, "iris")
                && ContainsComponent(knownNames, "sodium");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect installed optimization mods");
            IsIrisSodiumInstalled = false;
        }
    }

    private static bool ContainsComponent(IEnumerable<string?> names, string component) =>
        names.Any(name => !string.IsNullOrWhiteSpace(name)
            && name.Contains(component, StringComparison.OrdinalIgnoreCase));

    private async Task RefreshControllerSupportStateAsync()
    {
        if (SelectedProfile == null)
        {
            IsControllerConnected = false;
            IsControllerModInstalled = false;
            return;
        }

        try
        {
            IsControllerConnected = await _controllerDetection.IsAnyControllerConnectedAsync().ConfigureAwait(false);
            IsControllerModInstalled = await IsControllerModInstalledAsync(SelectedProfile.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect controller support state");
            IsControllerConnected = false;
            IsControllerModInstalled = false;
        }
    }

    private async Task<bool> IsControllerModInstalledAsync(int profileId)
    {
        try
        {
            var mods = await _modService.GetModsAsync(profileId).ConfigureAwait(false);
            var knownNames = mods
                .Where(m => m.Status == ShoroCraftLauncher.Core.Enums.ModStatus.Active)
                .SelectMany(m => new[] { m.Name, m.FileName, m.ModVersion });

            var modsDir = _minecraftService.GetModsDirectory(GetSelectedProfileGameDirectory());
            if (Directory.Exists(modsDir))
            {
                knownNames = knownNames.Concat(Directory.GetFiles(modsDir, "*.jar")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!));
            }

            return ContainsComponent(knownNames, "controlify")
                || ContainsComponent(knownNames, "controllable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect installed controller mod");
            return false;
        }
    }

    private async Task InstallControllerSupportAsync()
    {
        if (SelectedProfile == null || IsControllerModInstalled)
            return;

        IsDownloading = true;
        ReadyStatus = "Instalando soporte de mando...";
        StatusMessage = "Buscando mod de soporte de mando (Controlify)...";
        try
        {
            var loader = SelectedProfile.Type.ToString().ToLowerInvariant();
            var results = await _modService.SearchModsAsync("modrinth", "Controlify", SelectedProfile.MinecraftVersion, loader);
            var toInstall = results.FirstOrDefault();

            if (toInstall == null && (loader == "forge" || loader == "neoforge"))
            {
                var alt = await _modService.SearchModsAsync("modrinth", "Controllable", SelectedProfile.MinecraftVersion, loader);
                toInstall = alt.FirstOrDefault();
            }

            if (toInstall == null)
            {
                StatusMessage = "No se encontró un mod de soporte de mando compatible con este perfil.";
                ReadyStatus = "Error";
                return;
            }

            await _modService.InstallFromSearchAsync(SelectedProfile.Id, toInstall, "modrinth");

            StatusMessage = "Soporte de mando instalado correctamente en tu perfil.";
            ReadyStatus = "Listo";
            _launcherService.Log("[INFO] Soporte de mando (Controlify) instalado correctamente.");
            await RefreshControllerSupportStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error instalando soporte de mando: {ex.Message}";
            ReadyStatus = "Error";
            _logger.LogError(ex, "Error installing controller support mod");
            _launcherService.Log($"[ERROR] Error instalando soporte de mando: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task InstallIris()
    {
        if (SelectedProfile == null) return;
        if (IsIrisSodiumInstalled)
        {
            StatusMessage = "Iris + Sodium ya está instalado en este perfil.";
            ReadyStatus = "Instalado";
            return;
        }

        IsDownloading = true;
        ReadyStatus = "Instalando Iris & Sodium...";
        StatusMessage = "Descargando Iris (Shaders) y Sodium (Rendimiento)...";
        try
        {
            var irisMods = await _modService.SearchModsAsync("modrinth", "iris", SelectedProfile.MinecraftVersion, "fabric");
            if (irisMods.Any())
                await _modService.InstallFromSearchAsync(SelectedProfile.Id, irisMods.First(), "modrinth");

            var sodiumMods = await _modService.SearchModsAsync("modrinth", "sodium", SelectedProfile.MinecraftVersion, "fabric");
            if (sodiumMods.Any())
                await _modService.InstallFromSearchAsync(SelectedProfile.Id, sodiumMods.First(), "modrinth");
            
            StatusMessage = "Iris y Sodium instalados correctamente en tu perfil Fabric.";
            ReadyStatus = "Listo";
            await UpdateComponentInstallStatesAsync();
            _launcherService.Log("[INFO] Iris y Sodium instalados correctamente.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error instalando Iris: {ex.Message}";
            ReadyStatus = "Error";
            _logger.LogError(ex, "Error installing Iris");
            _launcherService.Log($"[ERROR] Error instalando Iris: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task InstallFabricIrisSodium()
    {
        IsDownloading = true;
        try
        {
            // 1. Ensure we have a Fabric profile
            Profile? fabricProfile = SelectedProfile;
            if (fabricProfile == null || fabricProfile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Fabric)
            {
                fabricProfile = Profiles.FirstOrDefault(p => p.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Fabric);
                if (fabricProfile == null)
                {
                    ReadyStatus = "Creando perfil Fabric...";
                    StatusMessage = "No hay perfil Fabric. Creando uno nuevo...";
                    fabricProfile = new Profile
                    {
                        Name = "Fabric",
                        MinecraftVersion = "latest",
                        Type = ShoroCraftLauncher.Core.Enums.ProfileType.Fabric,
                        LoaderVersion = "latest",
                        MinRamMB = 1024,
                        MaxRamMB = 4096,
                        WindowWidth = 854,
                        WindowHeight = 480
                    };
                    await _profileRepo.CreateAsync(fabricProfile);
                    await _profileService.LoadProfilesAsync();
                    fabricProfile = Profiles.FirstOrDefault(p => p.Id == fabricProfile.Id) ?? fabricProfile;
                }
                SelectedProfile = fabricProfile;
            }

            var mcVersion = fabricProfile.MinecraftVersion;
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                ReadyStatus = "Resolviendo versi\u00f3n de Minecraft...";
                StatusMessage = "Obteniendo \u00faltima versi\u00f3n estable...";
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            // 2. Install Minecraft version if needed
            ReadyStatus = "Verificando Minecraft...";
            StatusMessage = $"Verificando Minecraft {mcVersion}...";
            var versionsDir = Path.Combine(GetSelectedProfileGameDirectory(), "versions", mcVersion);
            if (!Directory.Exists(versionsDir) || !File.Exists(Path.Combine(versionsDir, $"{mcVersion}.jar")))
            {
                ReadyStatus = $"Instalando Minecraft {mcVersion}...";
                StatusMessage = $"Descargando Minecraft {mcVersion}...";
                var progress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallVersionAsync(mcVersion, progress, GetSelectedProfileGameDirectory());
            }

            // 3. Install Fabric loader if needed
            var loaderReady = IsLoaderInstalledAsync(fabricProfile, mcVersion);
            if (!loaderReady)
            {
                ReadyStatus = "Instalando Fabric...";
                StatusMessage = "Descargando e instalando Fabric loader...";
                var loaderVersion = await _minecraftService.ResolveLatestLoaderVersionAsync("Fabric", mcVersion);
                if (loaderVersion.Equals("latest"))
                    throw new Exception("No se pudo determinar la \u00faltima versi\u00f3n de Fabric.");

                var javaPath = fabricProfile.JavaPath;
                if (string.IsNullOrEmpty(javaPath))
                {
                    javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion);
                    if (string.IsNullOrEmpty(javaPath))
                        throw new Exception("No se encontr\u00f3 Java instalado.");
                }

                var loaderProgress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallLoaderAsync(
                    mcVersion, "Fabric", loaderVersion, javaPath,
                    msg => App.Current.Dispatcher.Invoke(() => LogStatus(msg)),
                    loaderProgress,
                    onLog: _launcherService.Log,
                    gameDir: GetSelectedProfileGameDirectory());

                fabricProfile.Type = ShoroCraftLauncher.Core.Enums.ProfileType.Fabric;
                fabricProfile.LoaderVersion = loaderVersion;
                fabricProfile.MinecraftVersion = mcVersion;
                await _profileService.UpdateProfileAsync(fabricProfile);
            }

            // 4. Install Iris + Sodium
            if (!IsIrisSodiumInstalled)
            {
                ReadyStatus = "Instalando Iris + Sodium...";
                StatusMessage = "Descargando Iris (Shaders) y Sodium (Rendimiento)...";

                var modrinthVersion = MinecraftVersions.ToModrinthVersion(mcVersion);

                var irisMods = await _modService.SearchModsAsync("modrinth", "iris", modrinthVersion, "fabric");
                if (irisMods.Any())
                    await _modService.InstallFromSearchAsync(fabricProfile.Id, irisMods.First(), "modrinth");

                var sodiumMods = await _modService.SearchModsAsync("modrinth", "sodium", modrinthVersion, "fabric");
                if (sodiumMods.Any())
                    await _modService.InstallFromSearchAsync(fabricProfile.Id, sodiumMods.First(), "modrinth");
            }

            ReadyStatus = "Listo";
            StatusMessage = "Fabric + Iris + Sodium instalados correctamente.";
            _launcherService.Log("[INFO] Fabric + Iris + Sodium instalados correctamente.");
            await UpdateComponentInstallStatesAsync();
            await UpdateProfileDetailsAsync();
        }
        catch (OperationCanceledException)
        {
            ReadyStatus = "Error";
            StatusMessage = "La instalaci\u00f3n tard\u00f3 demasiado y fue cancelada.";
            _launcherService.Log("[ERROR] Instalaci\u00f3n de Fabric+Iris+Sodium cancelada por timeout.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Fabric + Iris + Sodium");
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando Fabric+Iris+Sodium: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

}
