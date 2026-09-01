using System.IO.Compression;

namespace ShoroCraftLauncher.Infrastructure.Downloading;

public static class DownloadPathGuard
{
    private const int DefaultMaxZipEntries = 20_000;
    private const long DefaultMaxUncompressedBytes = 4L * 1024 * 1024 * 1024;

    public static string SafeFileName(string fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        if (name.Length == 0 || name is "." or "..")
            throw new Exception($"El nombre de archivo no es válido: '{fileName}'");

        // Rechaza separadores de ruta y caracteres inválidos en el nombre ORIGINAL,
        // antes de que Path.GetFileName pueda recortar componentes silenciosamente.
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (name.Contains(c))
                throw new Exception($"El nombre de archivo contiene caracteres no válidos: '{fileName}'");
        }

        return name;
    }

    public static string SafeRelativePath(string relativePath)
    {
        var input = relativePath ?? string.Empty;
        var normalized = input.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(input))
            throw new Exception($"La ruta no es válida: '{relativePath}'");

        normalized = normalized.TrimStart('/');

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new Exception($"La ruta no es válida: '{relativePath}'");

        foreach (var part in parts)
        {
            if (part is "." or "..")
                throw new Exception($"La ruta contiene segmentos no válidos: '{relativePath}'");

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                if (part.Contains(c))
                    throw new Exception($"La ruta contiene caracteres no válidos: '{relativePath}'");
            }
        }

        return Path.Combine(parts);
    }

    public static bool IsInsideDirectory(string rootDirectory, string targetPath)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(targetPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return target.Equals(root, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void ExtractZipToDirectorySafe(
        string zipPath,
        string destinationDirectory,
        bool overwrite = false,
        int maxEntries = DefaultMaxZipEntries,
        long maxUncompressedBytes = DefaultMaxUncompressedBytes)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        ExtractZipToDirectorySafe(archive, destinationDirectory, string.Empty, overwrite, maxEntries, maxUncompressedBytes);
    }

    public static void ExtractZipToDirectorySafe(
        ZipArchive archive,
        string destinationDirectory,
        string rootPrefix = "",
        bool overwrite = false,
        int maxEntries = DefaultMaxZipEntries,
        long maxUncompressedBytes = DefaultMaxUncompressedBytes,
        Func<string, bool>? shouldSkipRelativePath = null)
    {
        Directory.CreateDirectory(destinationDirectory);
        var entryCount = 0;
        var totalBytes = 0L;
        var prefix = rootPrefix.Replace('\\', '/').TrimStart('/');

        foreach (var entry in archive.Entries)
        {
            var entryName = entry.FullName.Replace('\\', '/');
            if (entryName.EndsWith('/')) continue;

            if (!string.IsNullOrEmpty(prefix))
            {
                if (!entryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                entryName = entryName[prefix.Length..];
            }

            if (string.IsNullOrWhiteSpace(entryName)) continue;

            entryCount++;
            if (entryCount > maxEntries)
                throw new Exception("El archivo ZIP contiene demasiados archivos.");

            totalBytes += entry.Length;
            if (totalBytes > maxUncompressedBytes)
                throw new Exception("El archivo ZIP es demasiado grande al descomprimirse.");

            var safeRelativePath = SafeRelativePath(entryName);
            if (shouldSkipRelativePath?.Invoke(safeRelativePath) == true)
                continue;

            var target = Path.Combine(destinationDirectory, safeRelativePath);
            if (!IsInsideDirectory(destinationDirectory, target))
                throw new Exception("El archivo ZIP contiene rutas no válidas.");

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(target, overwrite);
        }
    }
}
