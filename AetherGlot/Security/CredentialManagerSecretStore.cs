using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AetherGlot.Security;

/// <summary>
/// Stores secrets as generic credentials in Windows Credential Manager
/// (target "AetherGlot:&lt;name&gt;", persisted for the local machine user).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialManagerSecretStore : ISecretStore
{
    private const string TargetPrefix        = "AetherGlot:";
    private const uint   CredTypeGeneric     = 1;
    private const uint   CredPersistLocalMachine = 2;
    private const int    ErrorNotFound       = 1168;

    public string? Get(string name)
    {
        if (!CredRead(TargetPrefix + name, CredTypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, $"CredRead failed for {name}.");
        }

        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Delete(name);
            return;
        }

        var size = value.Length * sizeof(char);
        var blob = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var cred = new Credential
            {
                Type               = CredTypeGeneric,
                TargetName         = TargetPrefix + name,
                CredentialBlob     = blob,
                CredentialBlobSize = (uint)size,
                Persist            = CredPersistLocalMachine,
                UserName           = "AetherGlot",
            };
            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWrite failed for {name}.");
        }
        finally
        {
            // Zeroes the unmanaged copy before freeing it
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public void Delete(string name)
    {
        if (!CredDelete(TargetPrefix + name, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, $"CredDelete failed for {name}.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
