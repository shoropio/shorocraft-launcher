using System.Security.Cryptography;

namespace ShoroCraftLauncher.App.Services;

public static class SecretProtector
{
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var bytes = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plainText),
            null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(stored),
                null,
                DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Migración: valor guardado en texto plano antes de esta versión.
            return stored;
        }
    }
}
