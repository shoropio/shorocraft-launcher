using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public partial class ModService
{
    private static string ToModrinthVersion(string mcVersion)
        => MinecraftVersions.ToModrinthVersion(mcVersion);

    public async Task<ModCompatibilityResult> CheckAndDisableIncompatibleModsAsync(int profileId, string targetMcVersion)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");

        var modsDir = await GetModsFolderAsync(profileId).ConfigureAwait(false);
        if (!Directory.Exists(modsDir))
            return new ModCompatibilityResult { Checked = 0, Disabled = new List<string>(), Errors = new List<string>() };

        var allMods = Directory.GetFiles(modsDir, "*.jar");
        var disabled = new List<string>();
        var errors = new List<string>();
        var checkedCount = 0;

        foreach (var modFile in allMods)
        {
            try
            {
                checkedCount++;
                var mcVersion = await ExtractMcVersionFromModAsync(modFile).ConfigureAwait(false);
                
                if (mcVersion != null && !IsVersionCompatible(mcVersion, targetMcVersion))
                {
                    var fileName = Path.GetFileName(modFile);
                    var disabledPath = modFile + ".disabled";
                    
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(modFile, disabledPath);
                    
                    disabled.Add($"{fileName} (mod MC: {mcVersion}, target: {targetMcVersion})");
                    _logService.Warning("ModService", "CompatibilityCheck", 
                        $"Disabled incompatible mod: {fileName} (mod MC: {mcVersion}, target: {targetMcVersion})");
                }
            }
            catch (Exception ex)
            {
                var err = $"Error checking {Path.GetFileName(modFile)}: {ex.Message}";
                errors.Add(err);
                _logService.Error("ModService", "CompatibilityCheck", err, ex);
            }
        }

        if (disabled.Count > 0 || errors.Count > 0)
        {
            _logService.Info("ModService", "CompatibilityCheck", 
                $"Compatibility check complete: {checkedCount} checked, {disabled.Count} disabled, {errors.Count} errors");
        }

        return new ModCompatibilityResult
        {
            Checked = checkedCount,
            Disabled = disabled,
            Errors = errors
        };
    }

    public async Task<List<ModUpdateInfo>> CheckForUpdatesAsync(int profileId, string mcVersion)
    {
        var mods = (await _modRepository.GetByProfileIdAsync(profileId).ConfigureAwait(false))
            .Where(m => m.Status == ModStatus.Active && !string.IsNullOrEmpty(m.RemoteProjectId))
            .ToList();
        // Reset update status for all mods in a single pass (evita N+1)
        foreach (var mod in mods)
        {
            mod.LatestVersion = null;
            mod.HasUpdate = false;
            await _modRepository.UpdateAsync(mod).ConfigureAwait(false);
        }
        var modrinthVersion = MinecraftVersions.ToModrinthVersion(mcVersion);
        var updates = new List<ModUpdateInfo>();

        foreach (var mod in mods)
        {
            try
            {
                var url = $"https://api.modrinth.com/v2/project/{mod.RemoteProjectId}/version?game_versions=%5B%22{modrinthVersion}%22%5D&loaders=%5B%22fabric%22%5D";
                using var request = CreateModrinthRequest(url);
                await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.ValueKind.Equals(JsonValueKind.Array)) continue;

                var versions = doc.RootElement.EnumerateArray().ToList();
                if (versions.Count == 0) continue;

                var best = versions.FirstOrDefault(v =>
                    v.TryGetProperty("version_type", out var vt) && vt.GetString() == "release");
                if (best.ValueKind.Equals(JsonValueKind.Undefined))
                    best = versions[0];

                var latestVersion = "";
                if (best.TryGetProperty("version_number", out var vn))
                    latestVersion = vn.GetString() ?? "";

                if (!string.IsNullOrEmpty(latestVersion) && !latestVersion.Equals(mod.ModVersion, StringComparison.OrdinalIgnoreCase))
                {
                    mod.LatestVersion = latestVersion;
                    mod.HasUpdate = true;
                    await _modRepository.UpdateAsync(mod).ConfigureAwait(false);
                    updates.Add(new ModUpdateInfo(
                        mod.Id, mod.Name, mod.ModVersion, latestVersion,
                        mod.SourceProvider ?? "modrinth", mod.RemoteProjectId, mod.RemoteSlug));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check update for mod {Mod}", mod.Name);
            }
        }

        if (updates.Count > 0)
            _logService.Info("ModService", "UpdateCheck", $"Found {updates.Count} mod(s) with updates available.");

        return updates;
    }

    public async Task<Mod?> UpdateModAsync(int modId, string mcVersion)
    {
        var mod = await _modRepository.GetByIdAsync(modId).ConfigureAwait(false);
        if (mod == null || string.IsNullOrEmpty(mod.RemoteProjectId)) return null;

        _logService.Info("ModService", "UpdateMod", $"Updating '{mod.Name}'...");

        var profile = await _profileRepository.GetByIdAsync(mod.ProfileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {mod.ProfileId} not found");

        var searchResult = new Mod
        {
            Name = mod.Name,
            FileName = mod.RemoteProjectId,
            IconPath = mod.IconPath,
            Description = mod.Description
        };

        var updated = await InstallFromSearchAsync(mod.ProfileId, searchResult, mod.SourceProvider ?? "modrinth").ConfigureAwait(false);
        _logService.Info("ModService", "UpdateMod", $"'{mod.Name}' updated to {updated.ModVersion}.");
        return updated;
    }

    private async Task<string?> ExtractMcVersionFromModAsync(string modPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(modPath);
            
            // Try fabric.mod.json first (Fabric mods)
            var fabricEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                var (_, mcVersion, _) = ParseFabricModJson(content);
                if (!string.IsNullOrEmpty(mcVersion)) return mcVersion;
            }

            // Try mcmod.info (Forge/FML mods)
            var mcmodEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals("mcmod.info", StringComparison.OrdinalIgnoreCase));
            if (mcmodEntry != null)
            {
                using var reader = new StreamReader(mcmodEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                var (_, mcVersion, _) = ParseMcmodInfo(content);
                if (!string.IsNullOrEmpty(mcVersion)) return mcVersion;
            }
        }
        catch { }

        // Fallback: extract MC version from filename pattern (e.g., "iris-fabric-1.11.3+mc26.1.2.jar")
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(modPath);
            var mcIndex = fileName.LastIndexOf("+mc", StringComparison.OrdinalIgnoreCase);
            if (mcIndex >= 0)
            {
                var mcVer = fileName.Substring(mcIndex + 3);
                // Take only the version part (stop at next non-version char)
                var match = System.Text.RegularExpressions.Regex.Match(mcVer, @"^[\d.]+");
                if (match.Success) return match.Value;
            }
        }
        catch { }

        return null;
    }

    private static bool IsVersionCompatible(string modMcVersion, string targetMcVersion)
    {
        if (string.IsNullOrEmpty(modMcVersion) || string.IsNullOrEmpty(targetMcVersion))
            return true; // Unknown = assume compatible

        // Normalize both versions
        var modVersion = NormalizeVersion(modMcVersion);
        var targetVersion = NormalizeVersion(targetMcVersion);

        // Exact match
        if (modVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle wildcard "*"
        if (modVersion.Contains("*")) return true;

        // For MC 26.x versions (new versioning): require major.minor match
        // 26.1 ≠ 26.2 (they are distinct releases, not sub-versions)
        if (modVersion.StartsWith("1.21.", StringComparison.OrdinalIgnoreCase) &&
            targetVersion.StartsWith("1.21.", StringComparison.OrdinalIgnoreCase))
        {
            var modParts = modVersion.Split('.');
            var targetParts = targetVersion.Split('.');
            // Compare major.minor only (first 2 parts)
            if (modParts.Length >= 2 && targetParts.Length >= 2)
                return modParts[0] == targetParts[0] && modParts[1] == targetParts[1];
        }

        // Handle version ranges like "1.21" matching "1.21.1"
        var modPartsLegacy = modVersion.Split('.');
        var targetPartsLegacy = targetVersion.Split('.');

        // If mod version is a prefix of target (e.g., mod "1.21" targets "1.21.1")
        if (modPartsLegacy.Length <= targetPartsLegacy.Length)
        {
            bool prefixMatch = true;
            for (int i = 0; i < modPartsLegacy.Length; i++)
            {
                if (!modPartsLegacy[i].Equals(targetPartsLegacy[i], StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatch = false;
                    break;
                }
            }
            if (prefixMatch) return true;
        }

        return false;
    }

    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return version;
        return MinecraftVersions.ToModrinthVersion(version);
    }
}
