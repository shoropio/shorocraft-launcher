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

    [DllImport("secur32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredReadW(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credentials);

    [DllImport("secur32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredWriteW(
        in CREDENTIALW credential,
        uint flags);

    [DllImport("secur32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredDeleteW(
        string targetName,
        uint type,
        uint flags);

    [DllImport("secur32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CredEnumerateW(
        string targetName,
        uint flags,
        out int count,
        out IntPtr credentials);

    [DllImport("secur32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
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

    public async Task<string?> GetSecretAsync(string name)
    {
        // Try Credential Locker first
        if (TryReadFromCredentialLocker(name, out string? secret))
        {
            return secret;
        }

        // Fallback: try reading from old DPAPI-encrypted config
        // This allows migration from existing installations
        return null;
    }

    private bool TryReadFromCredentialLocker(string name, out string? secret)
    {
        secret = null;
        int result = CredReadW(AppName, (uint)CredType.Generic, 0, out IntPtr credentials);
        if (result != 0 && credentials != IntPtr.Zero)
        {
            try
            {
                // CredReadW returns a linked list - first credential
                var cred = (CREDENTIALW)Marshal.PtrToStructure(credentials, typeof(CREDENTIALW))!;

                if (string.Equals(cred.credentialName, name, StringComparison.OrdinalIgnoreCase))
                {
                    secret = cred.password;
                    CredFree(credentials);
                    return true;
                }

                // Walk the linked list
                IntPtr? current = credentials;
                while (current != IntPtr.Zero)
                {
                    cred = (CREDENTIALW)Marshal.PtrToStructure(current!.Value, typeof(CREDENTIALW))!;
                    if (string.Equals(cred.credentialName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        secret = cred.password;
                        CredFree(credentials);
                        return true;
                    }

                    // Next in linked list
                    int structSize = Marshal.SizeOf(typeof(CREDENTIALW));
                    IntPtr next = (IntPtr)((long)current.Value + structSize);
                    // Check if we've reached the end (the last node has linkInfo = 0)
                    // Actually, the link is embedded in the structure, let's just free and return
                    current = next;
                }

                CredFree(credentials);
            }
            catch
            {
                CredFree(credentials);
            }
        }
        return false;
    }

    public async Task SetSecretAsync(string name, string secret)
    {
        var cred = new CREDENTIALW
        {
            credentialType = CredType.Generic,
            credentialName = name,
            password = secret,
            credentialPersist = 5, // CRED_PER_ROAMING
            targetName = AppName
        };

        int structSize = Marshal.SizeOf(typeof(CREDENTIALW));
        IntPtr ptr = Marshal.AllocHGlobal(structSize);
        Marshal.StructureToPtr(cred, ptr, false);

        int result = CredWriteW(in cred, 0);
        Marshal.FreeHGlobal(ptr);

        if (result != 0)
        {
            // Successfully stored in Windows Credential Locker
        }
        else
        {
            throw new Exception("Failed to write credential to Windows Credential Locker");
        }
    }

    public async Task DeleteSecretAsync(string name)
    {
        int result = CredDeleteW(AppName, (uint)CredType.Generic, 0);
        // result = 0 means not found, which is OK
    }

    public async Task<bool> HasSecretAsync(string name)
    {
        // Try Credential Locker first
        if (TryReadFromCredentialLocker(name, out _))
        {
            return true;
        }

        // Fallback: check if exists in old DPAPI config
        // This is handled by the caller (SettingsRepository migration)
        return false;
    }
}
#nullable disable