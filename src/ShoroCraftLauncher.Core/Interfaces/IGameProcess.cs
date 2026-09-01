namespace ShoroCraftLauncher.Core.Interfaces;

/// <summary>
/// Abstracción de un proceso de juego en ejecución.
/// Evita exponer <c>System.Diagnostics.Process</c> desde Core.
/// </summary>
public interface IGameProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    string FileName { get; }
    string Arguments { get; }

    /// <summary>Se dispara por cada línea recibida en stdout.</summary>
    event Action<string?>? OutputLineReceived;

    /// <summary>Se dispara por cada línea recibida en stderr.</summary>
    event Action<string?>? ErrorLineReceived;

    /// <summary>Se dispara cuando el proceso termina. El argumento es el código de salida.</summary>
    event Action<int>? Exited;

    /// <summary>Inicia el proceso y comienza la lectura asíncrona de stdout/stderr.</summary>
    void Start();

    /// <summary>Termina el proceso y todo su árbol de procesos hijos.</summary>
    void Kill();

    Task WaitForExitAsync();
}
