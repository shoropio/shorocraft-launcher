namespace ShoroCraftLauncher.Infrastructure.Downloading;

public static class DownloadPathGuard
{
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
}
