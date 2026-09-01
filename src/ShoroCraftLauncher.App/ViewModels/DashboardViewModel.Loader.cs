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
    private async Task InstallLoader(string? loaderArg)
    {
        if (string.IsNullOrEmpty(loaderArg) || SelectedProfile == null) return;
        if (IsDownloading || IsBusy) return;
        var parts = loaderArg.Split(':');
        if (parts.Length < 2) return;

        var loaderType = parts[0];
        var loaderVersion = parts[1];

        IsDownloading = true;
        ReadyStatus = $"Preparando {loaderType}...";
        _launcherService.Log($"[INFO] Preparando instalación de {loaderType}...");
        StatusMessage = $"Preparando instalación de {loaderType}...";
        try
        {
            var mcVersion = SelectedProfile.MinecraftVersion;
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versión estable más nueva de Minecraft...");
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            ReadyStatus = $"Instalando {loaderType}...";

            if (loaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Obteniendo última versión de {loaderType}...";
                _launcherService.Log($"[INFO] Obteniendo la versión estable más nueva de {loaderType} para Minecraft {mcVersion}...");
                var resolved = await _minecraftService.ResolveLatestLoaderVersionAsync(loaderType, mcVersion);
                if (resolved.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"No se pudo determinar la última versión de {loaderType} para Minecraft {mcVersion}. Es posible que {loaderType} no tenga soporte para esa versión.");
                loaderVersion = resolved;
                StatusMessage = $"{loaderType} {loaderVersion} encontrado.";
                _launcherService.Log($"[INFO] {loaderType} {loaderVersion} encontrado.");
            }

            var javaPath = SelectedProfile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                LogStatus($"Buscando Java recomendado para Minecraft {mcVersion}...");
                javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion);
                if (string.IsNullOrEmpty(javaPath))
                    throw new Exception("No se encontró Java instalado. Descarga e instala Java 17+ desde adoptium.net");
            }

            var progress = new Progress<double>(p => DownloadProgress = p);
            _launcherService.Log($"[INFO] Java seleccionado: {javaPath}");
            await _minecraftService.InstallLoaderAsync(
                mcVersion, loaderType, loaderVersion, javaPath,
                msg => { App.Current.Dispatcher.Invoke(() => LogStatus(msg)); },
                progress,
                onLog: _launcherService.Log,
                gameDir: GetSelectedProfileGameDirectory());

            var loaderEnum = Enum.TryParse<ShoroCraftLauncher.Core.Enums.ProfileType>(loaderType, ignoreCase: true, out var parsed)
                ? parsed : SelectedProfile.Type;
            SelectedProfile.Type = loaderEnum;
            SelectedProfile.LoaderVersion = loaderVersion;
            SelectedProfile.MinecraftVersion = mcVersion;
            await _profileService.UpdateProfileAsync(SelectedProfile);
                
            ReadyStatus = "Listo";
            StatusMessage = $"{loaderType} {loaderVersion} instalado correctamente.";
            _launcherService.Log($"[INFO] {loaderType} {loaderVersion} instalado correctamente.");
            await UpdateComponentInstallStatesAsync();
        }
        catch (OperationCanceledException)
        {
            ReadyStatus = "Error";
            StatusMessage = $"La instalación de {loaderType} tardó demasiado y fue cancelada.";
            _launcherService.Log($"[ERROR] La instalación de {loaderType} tardó demasiado y fue cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install {Loader}", loaderType);
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando {loaderType}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private bool IsLoaderInstalledAsync(Profile profile, string targetVersion)
    {
        var mcPath = new CmlLib.Core.MinecraftPath(GetSelectedProfileGameDirectory());
        var loaderPrefix = profile.Type.ToString().ToLower();
        var dirs = Directory.Exists(mcPath.Versions)
            ? Directory.GetDirectories(mcPath.Versions)
            : Array.Empty<string>();
        var match = dirs.Select(Path.GetFileName)
                        .FirstOrDefault(n => n != null
                            && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                                targetVersion,
                                StringComparison.OrdinalIgnoreCase));
        return match != null;
    }

    private async Task ValidateProfileChecklistAsync()
    {
        if (SelectedProfile == null)
        {
            IsJavaReady = false;
            IsVersionReady = false;
            IsLoaderReady = false;
            IsRamReady = false;
            ChecklistMessage = "Selecciona un perfil.";
            return;
        }

        try
        {
            var targetVersion = SelectedProfile.MinecraftVersion;
            if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            var gameDir = GetSelectedProfileGameDirectory();

            // 1. RAM check
            IsRamReady = SelectedProfile.MaxRamMB >= 1024 && SelectedProfile.MinRamMB <= SelectedProfile.MaxRamMB;

            // 2. Java check
            var javaPath = SelectedProfile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = await _javaService.GetRecommendedJavaPathAsync(targetVersion);
            }
            IsJavaReady = !string.IsNullOrEmpty(javaPath) && System.IO.File.Exists(javaPath);

            // 3. Version check
            var mcPath = new CmlLib.Core.MinecraftPath(gameDir);
            var versionDir = System.IO.Path.Combine(mcPath.Versions, targetVersion);
            var jsonPath = System.IO.Path.Combine(versionDir, $"{targetVersion}.json");
            var jarPath = System.IO.Path.Combine(versionDir, $"{targetVersion}.jar");
            IsVersionReady = System.IO.File.Exists(jsonPath) && System.IO.File.Exists(jarPath);

            // 4. Loader check
            if (SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
            {
                IsLoaderReady = true;
            }
            else
            {
                var loaderPrefix = SelectedProfile.Type.ToString().ToLower();
                var dirs = System.IO.Directory.Exists(mcPath.Versions) 
                    ? System.IO.Directory.GetDirectories(mcPath.Versions) 
                    : Array.Empty<string>();
                var match = dirs.Select(System.IO.Path.GetFileName)
                                .FirstOrDefault(n => n != null
                                    && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(
                                        n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                                        targetVersion,
                                        StringComparison.OrdinalIgnoreCase));
                IsLoaderReady = match != null;
            }

            // 4b. Loader update check (cached to avoid flicker from repeated validations)
            var profile = SelectedProfile;
            if (profile != null && IsLoaderReady
                && profile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla
                && !string.IsNullOrEmpty(profile.LoaderVersion))
            {
                var updateKey = $"{profile.Id}|{targetVersion}|{profile.Type}|{profile.LoaderVersion}";
                if (updateKey != _loaderUpdateCacheKey)
                {
                    await _loaderUpdateLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (updateKey != _loaderUpdateCacheKey)
                        {
                            string? latestLoader = null;
                            var ok = false;
                            try
                            {
                                latestLoader = await _minecraftService.CheckLoaderUpdateAsync(
                                    profile.Type.ToString(), targetVersion, profile.LoaderVersion).ConfigureAwait(false);
                                ok = true;
                            }
                            catch
                            {
                                // Transient network failure: keep previous visible state to avoid flicker
                            }

                            if (ok)
                            {
                                _loaderUpdateCacheKey = updateKey;
                                _loaderUpdateCacheVersion = latestLoader;
                            }
                        }
                    }
                    finally
                    {
                        _loaderUpdateLock.Release();
                    }
                }

                if (_loaderUpdateCacheKey == updateKey)
                {
                    if (!string.IsNullOrEmpty(_loaderUpdateCacheVersion))
                    {
                        HasLoaderUpdate = true;
                        LoaderUpdateVersion = _loaderUpdateCacheVersion;
                        LoaderUpdateMessage = $"{profile.Type} {profile.LoaderVersion} → {_loaderUpdateCacheVersion}";
                        NotifyLoaderUpdateAvailable(profile, _loaderUpdateCacheVersion, targetVersion);
                    }
                    else
                    {
                        HasLoaderUpdate = false;
                        LoaderUpdateVersion = null;
                        LoaderUpdateMessage = string.Empty;
                        _loaderToastKey = null;
                    }
                }
                // else: check failed and not yet cached -> preserve previous state (no flicker)
            }
            else
            {
                HasLoaderUpdate = false;
                LoaderUpdateVersion = null;
                LoaderUpdateMessage = string.Empty;
                _loaderUpdateCacheKey = null;
                _loaderUpdateCacheVersion = null;
                _loaderToastKey = null;
            }

            if (IsJavaReady && IsVersionReady && IsLoaderReady && IsRamReady)
            {
                ChecklistMessage = "Perfil listo para jugar.";
            }
            else
            {
                var missing = new List<string>();
                if (!IsJavaReady) missing.Add("Java");
                if (!IsVersionReady) missing.Add("Minecraft");
                if (!IsLoaderReady) missing.Add(SelectedProfile.Type.ToString());
                if (!IsRamReady) missing.Add("Asignación de RAM");
                ChecklistMessage = "Falta: " + string.Join(", ", missing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate profile checklist");
            ChecklistMessage = "Error al validar estado.";
        }
    }

    private async Task UpdateLoader()
    {
        if (SelectedProfile == null || !HasLoaderUpdate || string.IsNullOrEmpty(LoaderUpdateVersion)) return;
        await UpdateLoaderForAsync(SelectedProfile, LoaderUpdateVersion, SelectedProfile.MinecraftVersion);
    }

    private async Task UpdateLoaderForAsync(Profile profile, string newVersion, string mcVersion)
    {
        if (profile == null || string.IsNullOrEmpty(newVersion)) return;
        IsBusy = true;
        IsDownloading = true;
        ReadyStatus = $"Actualizando {profile.Type}...";

        try
        {
            var gameDir = GetSelectedProfileGameDirectory();
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");

            var javaPath = profile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
                javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion);

            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                ReadyStatus = "Java no encontrado.";
                return;
            }

            await _minecraftService.UpdateLoaderAsync(
                mcVersion, profile.Type.ToString(), newVersion,
                javaPath, gameDir, msg => LogStatus(msg));

            profile.LoaderVersion = newVersion;
            await _profileService.UpdateProfileAsync(profile);

            ReadyStatus = $"{profile.Type} actualizado a {newVersion}.";
            HasLoaderUpdate = false;
            LoaderUpdateVersion = null;
            LoaderUpdateMessage = string.Empty;
            _loaderToastKey = null;

            await ValidateProfileChecklistAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update loader");
            ReadyStatus = $"Error al actualizar loader: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsDownloading = false;
        }
    }

    private void NotifyLoaderUpdateAvailable(Profile profile, string newVersion, string mcVersion)
    {
        var key = $"{profile.Id}|{newVersion}";
        bool shouldNotify;
        lock (_loaderToastLock)
        {
            shouldNotify = _loaderToastKey != key;
            if (shouldNotify) _loaderToastKey = key;
        }
        if (!shouldNotify) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var gameDir = GetSelectedProfileGameDirectory();
                var javaPath = profile.JavaPath;
                if (string.IsNullOrEmpty(javaPath))
                    javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(javaPath))
                {
                    await _minecraftService.PreDownloadLoaderInstallerAsync(mcVersion, profile.Type.ToString(), newVersion, gameDir).ConfigureAwait(false);
                }
            }
            catch
            {
                // La pre-descarga es best-effort; el aviso se muestra igualmente.
            }

            var toast = new ToastItem(
                "Actualización del cargador disponible",
                $"{profile.Type} {profile.LoaderVersion} → {newVersion} para el perfil '{profile.Name}'. ¿Instalar ahora?",
                ToastSeverity.Warning,
                duration: null);
            toast.Actions = new List<ToastAction>
            {
                new ToastAction("Instalar", new RelayCommand(_ =>
                {
                    _toastService.Dismiss(toast.Id);
                    _ = UpdateLoaderForAsync(profile, newVersion, mcVersion);
                })),
                new ToastAction("Después", new RelayCommand(_ => _toastService.Dismiss(toast.Id)))
            };
            _toastService.ShowToast(toast);
        });
    }

}
