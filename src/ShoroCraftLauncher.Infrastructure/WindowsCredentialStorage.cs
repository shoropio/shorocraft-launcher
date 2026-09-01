using ShoroCraftLauncher.Core.Interfaces;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#nullable enable
namespace ShoroCraftLauncher.Infrastructure;

public class WindowsCredentialStorage : ISecretStorage
{
    private const string AppName = "ShoroCraftLauncher";

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredReadW(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credentials);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredWriteW(
        in CREDENTIALW credential,
        uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredDeleteW(
        string targetName,
        uint type,
        uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredFree(IntPtr ptr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIALW
    {
        public uint dwFlags;
        public CredType credentialType;
        public uint dwReserved;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? credentialName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? credentialComment;
        public int credentialPersist;
        public int credentialAttributeCount;
        [MarshalAs(UnmanagedType.LPStr)]
        public IntPtr credentialAttributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? password;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? username;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? targetName;
    }

    private enum CredType : uint
    {
        Generic = 1,
        DomainPassword = 2,
        DomainCertificate = 3,
        DomainVisiblePassword = 4,
        GenericTargetCredential = 5,
        DomainExtended = 6
    }

    private static string GetTargetName(string name)
        => $"{AppName}_{name}";

    public Task<string?> GetSecretAsync(string name) =>
        Task.Run(() =>
        {
            TryReadFromCredentialLocker(name, out string? secret);
            return secret;
        });

    private bool TryReadFromCredentialLocker(string name, out string? secret)
    {
        secret = null;
        string targetName = GetTargetName(name);
        int result = CredReadW(targetName, (uint)CredType.Generic, 0, out IntPtr credentials);
        if (result != 0 && credentials != IntPtr.Zero)
        {
            try
            {
                var cred = (CREDENTIALW)Marshal.PtrToStructure(credentials, typeof(CREDENTIALW))!;

                if (string.Equals(cred.credentialName, name, StringComparison.OrdinalIgnoreCase))
                {
                    secret = cred.password;
                    return true;
                }
            }
            finally
            {
                CredFree(credentials);
            }
        }
        return false;
    }

    public Task SetSecretAsync(string name, string secret) =>
        Task.Run(() =>
        {
            var cred = new CREDENTIALW
            {
                credentialType = CredType.Generic,
                credentialName = name,
                password = secret,
                credentialPersist = 5, // CRED_PER_ROAMING
                targetName = GetTargetName(name)
            };

            int result = CredWriteW(in cred, 0);

            if (result == 0)
            {
                throw new Exception("Failed to write credential to Windows Credential Locker");
            }
        });

    public Task DeleteSecretAsync(string name) =>
        Task.Run(() => CredDeleteW(GetTargetName(name), (uint)CredType.Generic, 0));

    public Task<bool> HasSecretAsync(string name) =>
        Task.Run(() => TryReadFromCredentialLocker(name, out _));
}
#nullable disable
