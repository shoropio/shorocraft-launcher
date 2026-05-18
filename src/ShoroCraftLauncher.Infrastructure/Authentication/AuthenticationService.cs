using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.Infrastructure.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;
    private const string CredentialTarget = "ShoroCraftLauncher_Minecraft";

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

            // Try to login silently first
            var session = await loginHandler.AuthenticateSilently();
            
            if (session == null)
            {
                // This would normally open a browser, but for a professional launcher
                // we want to integrate it better. For now, let's use the default browser flow.
                session = await loginHandler.Authenticate();
            }

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

            return new AuthResult { Success = false, ErrorMessage = "Login cancelado o fallido." };
        } catch (Exception ex) {
            _logger.LogError(ex, "Microsoft authentication failed");
            return new AuthResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<AuthResult> AuthenticateOfflineAsync(string username)
    {
        _logger.LogInformation("Offline authentication for {Username}", username);

        if (string.IsNullOrWhiteSpace(username))
            return new AuthResult { Success = false, ErrorMessage = "El nombre de usuario no puede estar vacío." };

        var uuid = GenerateOfflineUuid(username);

        return new AuthResult
        {
            Success = true,
            AccessToken = "offline",
            Uuid = uuid,
            Username = username
        };
    }

    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('|');
            if (parts.Length == 0) return false;

            return !string.IsNullOrEmpty(parts[0]);
        } catch {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var advapi32 = NativeLibrary.Load("advapi32.dll");
                var credentialRead = NativeLibrary.GetExport(advapi32, "CredReadW");
                var credentialWrite = NativeLibrary.GetExport(advapi32, "CredWriteW");
                var credentialDelete = NativeLibrary.GetExport(advapi32, "CredDeleteW");
                NativeLibrary.Free(advapi32);
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to clear stored credentials");
        }
    }

    public async Task<string?> GetStoredTokenAsync()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            return null;
        } catch {
            return null;
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
