using System.Threading.Tasks;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface ISecretStorage
{
    Task<string?> GetSecretAsync(string name);
    Task SetSecretAsync(string name, string secret);
    Task DeleteSecretAsync(string name);
    Task<bool> HasSecretAsync(string name);
}