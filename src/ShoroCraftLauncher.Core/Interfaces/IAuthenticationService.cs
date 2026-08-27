namespace ShoroCraftLauncher.Core.Interfaces;

public class AuthResult
{
    public bool Success { get; set; }
    public bool IsOffline { get; set; }
    public string? AccessToken { get; set; }
    public string? Uuid { get; set; }
    public string? Username { get; set; }
    public string? SkinUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAuthenticationService
{
    Task<AuthResult> AuthenticateAsync();
    Task<AuthResult> AuthenticateSilentlyAsync();
    AuthResult AuthenticateOfflineAsync(string username);
    Task<bool> ValidateTokenAsync(string accessToken);
    Task<AuthResult> ValidateAndRefreshAsync(AuthResult current);
    Task LogoutAsync();
}
