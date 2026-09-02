using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Minecraft;


public partial class MinecraftService : IMinecraftService
{
    #region Modelo de datos de versión

    private class VersionData : IDisposable
    {
        private readonly JsonDocument _doc;
        public string Id { get; }
        public string Url { get; }

        public VersionData(string id, string url, string json)
        {
            Id = id;
            Url = url;
            _doc = JsonDocument.Parse(json);
        }

        public void Dispose() => _doc.Dispose();

        public string? GetClientUrl()
        {
            if (_doc.RootElement.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("client", out var client)
                && client.TryGetProperty("url", out var url))
                return url.GetString();
            return null;
        }

        public string? GetServerUrl()
        {
            if (_doc.RootElement.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("server", out var server)
                && server.TryGetProperty("url", out var url))
                return url.GetString();
            return null;
        }

        public string GetMainClass()
        {
            if (_doc.RootElement.TryGetProperty("mainClass", out var mc))
                return mc.GetString() ?? "net.minecraft.client.main.Main";
            return "net.minecraft.client.main.Main";
        }

        public string GetAssetIndexId()
        {
            if (_doc.RootElement.TryGetProperty("assetIndex", out var ai)
                && ai.TryGetProperty("id", out var id))
                return id.GetString() ?? "1.21";
            return "1.21";
        }

        public string? GetAssetIndexUrl()
        {
            if (_doc.RootElement.TryGetProperty("assetIndex", out var ai)
                && ai.TryGetProperty("url", out var url))
                return url.GetString();
            return null;
        }

        public List<(string? Path, string? Url)> GetLibraries()
        {
            var result = new List<(string?, string?)>();
            if (!_doc.RootElement.TryGetProperty("libraries", out var libs)) return result;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!LibraryPassesRules(lib)) continue;
                if (lib.TryGetProperty("downloads", out var dl)
                    && dl.TryGetProperty("artifact", out var artifact))
                {
                    var path = artifact.TryGetProperty("path", out var p) ? p.GetString() : null;
                    var url = artifact.TryGetProperty("url", out var u) ? u.GetString() : null;
                    result.Add((path, url));
                }
            }
            return result;
        }

        public List<(string Path, string Url)> GetNativeLibraries(string osName)
        {
            var result = new List<(string, string)>();
            if (!_doc.RootElement.TryGetProperty("libraries", out var libs)) return result;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!LibraryPassesRules(lib)) continue;
                if (!lib.TryGetProperty("natives", out var natives)) continue;
                if (!natives.TryGetProperty(osName, out var classifierElement)) continue;
                var classifier = classifierElement.GetString();
                if (classifier == null) continue;

                if (!lib.TryGetProperty("downloads", out var dl)) continue;
                if (!dl.TryGetProperty("classifiers", out var classifiers)) continue;
                if (!classifiers.TryGetProperty(classifier, out var nativeEntry)) continue;

                var path = nativeEntry.TryGetProperty("path", out var p) ? p.GetString() : null;
                var url = nativeEntry.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (path != null && url != null)
                    result.Add((path, url));
            }
            return result;
        }

        private static bool LibraryPassesRules(JsonElement lib)
        {
            if (!lib.TryGetProperty("rules", out var rules)) return true;

            bool allowed = false;
            foreach (var rule in rules.EnumerateArray())
            {
                var action = rule.GetProperty("action").GetString();
                bool matches = true;

                if (rule.TryGetProperty("os", out var os))
                {
                    if (os.TryGetProperty("name", out var osName))
                        matches &= osName.GetString() == GetCurrentOsName();

                    if (os.TryGetProperty("arch", out var arch))
                    {
                        var is64Bit = Environment.Is64BitOperatingSystem;
                        matches &= (arch.GetString() == "x86" && !is64Bit)
                                || (arch.GetString() == "x86_64" && is64Bit);
                    }
                }

                if (action == "allow" && matches) allowed = true;
                else if (action == "disallow" && matches) return false;
            }
            return allowed;
        }
    }

    #endregion

}
