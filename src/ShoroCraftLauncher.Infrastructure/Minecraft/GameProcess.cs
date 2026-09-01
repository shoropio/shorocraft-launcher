using System.Diagnostics;
using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.Infrastructure.Minecraft;

/// <summary>
/// Wrapper de <see cref="Process"/> que expone la abstracción <see cref="IGameProcess"/>
/// para no filtrar <c>System.Diagnostics</c> a Core.
/// </summary>
public sealed class GameProcess : IGameProcess
{
    private readonly Process _process;

    public GameProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) => OutputLineReceived?.Invoke(e.Data);
        _process.ErrorDataReceived += (_, e) => ErrorLineReceived?.Invoke(e.Data);
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
    }

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    public string FileName => _process.StartInfo.FileName;
    public string Arguments => _process.StartInfo.Arguments;

    public event Action<string?>? OutputLineReceived;
    public event Action<string?>? ErrorLineReceived;
    public event Action<int>? Exited;

    public void Start()
    {
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Kill() => _process.Kill(entireProcessTree: true);

    public Task WaitForExitAsync() => _process.WaitForExitAsync();

    public void Dispose() => _process.Dispose();
}
