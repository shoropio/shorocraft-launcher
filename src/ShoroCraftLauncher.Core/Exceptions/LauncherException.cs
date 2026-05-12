namespace ShoroCraftLauncher.Core.Exceptions;

public class LauncherException : Exception
{
    public LauncherException(string message) : base(message) { }
    public LauncherException(string message, Exception inner) : base(message, inner) { }
}

public class JavaNotFoundException : LauncherException
{
    public JavaNotFoundException() : base("No se encontró una instalación válida de Java.") { }
}

public class MinecraftNotInstalledException : LauncherException
{
    public MinecraftNotInstalledException(string version) : base($"Minecraft {version} no está instalado.") { }
}

public class AuthenticationException : LauncherException
{
    public AuthenticationException(string message) : base(message) { }
}

public class DownloadException : LauncherException
{
    public DownloadException(string message, Exception inner) : base(message, inner) { }
}
