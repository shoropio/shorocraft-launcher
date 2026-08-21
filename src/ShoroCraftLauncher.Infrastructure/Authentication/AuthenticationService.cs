using System.Text;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.Infrastructure.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(ILogger<AuthenticationService> logger)
    {
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync()
    {
        _logger.LogInformation("Starting Microsoft authentication flow");

        try
        {
            var loginHandler = new CmlLib.Core.Auth.Microsoft.JELoginHandlerBuilder()
                .Build();

            try
            {
                var silentSession = await loginHandler.AuthenticateSilently().ConfigureAwait(false);
                if (silentSession != null)
                {
                    return new AuthResult
                    {
                        Success = true,
                        AccessToken = silentSession.AccessToken,
                        Uuid = silentSession.UUID,
                        Username = silentSession.Username
                    };
                }
            }
            catch (Exception ex) when (ex.Message.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Silent Microsoft authentication requires an interactive login");
            }

            var session = await loginHandler.Authenticate().ConfigureAwait(false);

            if (session != null)
            {
                return new AuthResult
                {
                    Success = true,
                    AccessToken = session.AccessToken,
                    Uuid = session.UUID,
                    Username = session.Username
                };
            }

            return new AuthResult { Success = false, ErrorMessage = "Inicio de sesión cancelado o fallido." };
        }
        catch (Exception ex)
        {
            if (IsCancellation(ex))
            {
                _logger.LogInformation("Microsoft authentication canceled by user");
                return new AuthResult { Success = false, ErrorMessage = "Inicio de sesión Microsoft cancelado." };
            }

            _logger.LogError(ex, "Microsoft authentication failed");
            return new AuthResult { Success = false, ErrorMessage = GetFriendlyMicrosoftAuthError(ex) };
        }
    }

    public async Task<AuthResult> AuthenticateSilentlyAsync()
    {
        _logger.LogInformation("Attempting silent Microsoft authentication");

        try
        {
            var loginHandler = new CmlLib.Core.Auth.Microsoft.JELoginHandlerBuilder()
                .Build();

            var session = await loginHandler.AuthenticateSilently().ConfigureAwait(false);
            if (session != null)
            {
                return new AuthResult
                {
                    Success = true,
                    AccessToken = session.AccessToken,
                    Uuid = session.UUID,
                    Username = session.Username
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Silent Microsoft authentication failed: {Message}", ex.Message);
        }

        return new AuthResult { Success = false, ErrorMessage = "No hay sesión guardada." };
    }

    private static bool IsCancellation(Exception ex)
    {
        foreach (var inner in Flatten(ex))
        {
            if (inner is OperationCanceledException) return true;
        }
        return false;
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var nested in Flatten(inner))
                    yield return nested;
            }
        }
        else
        {
            yield return ex;
            if (ex.InnerException != null)
            {
                foreach (var nested in Flatten(ex.InnerException))
                    yield return nested;
            }
        }
    }

    private static string GetFriendlyMicrosoftAuthError(Exception ex)
    {
        var messages = Flatten(ex).Select(e => e.Message);

        if ((ex.GetType().Name == "JEAuthException" &&
             messages.Any(m => m.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase))) ||
            messages.Any(m => m.Contains("Java edition not owned", StringComparison.OrdinalIgnoreCase)))
        {
            return "Esta cuenta no tiene Minecraft Java activo.";
        }

        if (messages.Any(m => m.Contains("default WebUI", StringComparison.OrdinalIgnoreCase) ||
                              m.Contains("WebView", StringComparison.OrdinalIgnoreCase)))
        {
            return "No se pudo abrir la ventana de inicio de sesión Microsoft. Verifica que Microsoft Edge WebView2 esté instalado.";
        }

        if (messages.Any(m => m.Contains("cancel", StringComparison.OrdinalIgnoreCase)))
            return "Inicio de sesión Microsoft cancelado.";

        return "No se pudo iniciar sesión con Microsoft. Revisa tu conexión e inténtalo de nuevo.";
    }

    public AuthResult AuthenticateOfflineAsync(string username)
    {
        _logger.LogInformation("Offline authentication for {Username}", username);

        if (string.IsNullOrWhiteSpace(username))
            return new AuthResult { Success = false, ErrorMessage = "El nombre de usuario no puede estar vacío." };

        var uuid = GenerateOfflineUuid(username);

        return new AuthResult
        {
            Success = true,
            IsOffline = true,
            AccessToken = "offline",
            Uuid = uuid,
            Username = username
        };
    }

    public Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('|');
            return Task.FromResult(parts.Length > 0 && !string.IsNullOrEmpty(parts[0]));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var loginHandler = new CmlLib.Core.Auth.Microsoft.JELoginHandlerBuilder()
                .Build();
            await loginHandler.Signout().ConfigureAwait(false);
            _logger.LogInformation("Microsoft session cleared");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear stored Microsoft session");
        }
    }

    private static string GenerateOfflineUuid(string username)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString();
    }
}
