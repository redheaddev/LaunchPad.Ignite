using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LaunchPad.Ignite.App.Services.Platform.Secrets;

namespace LaunchPad.Ignite.App.Services.Platform.Secrets.Windows;

internal sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string TargetPrefix = "LaunchPad.Ignite/";
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobSize = 2560;

    public IReadOnlyList<string> GetNames()
    {
        if (!NativeMethods.CredEnumerate(TargetPrefix + "*", 0, out var count, out var credentials))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return Array.Empty<string>();
            }

            throw new Win32Exception(error);
        }

        try
        {
            var names = new List<string>((int)count);
            for (var index = 0; index < count; index++)
            {
                var credentialPointer = Marshal.ReadIntPtr(credentials, checked((int)index * IntPtr.Size));
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                var targetName = credential.TargetName;
                if (targetName is not null && targetName.StartsWith(TargetPrefix, StringComparison.Ordinal))
                {
                    names.Add(targetName[TargetPrefix.Length..]);
                }
            }

            return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            NativeMethods.CredFree(credentials);
        }
    }

    public bool TryGet(string name, out string value)
    {
        if (!NativeMethods.CredRead(TargetPrefix + name, GenericCredential, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                value = string.Empty;
                return false;
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            var bytes = new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            }

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
        finally
        {
            NativeMethods.CredFree(credentialPointer);
        }
    }

    public void Set(string name, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumCredentialBlobSize)
        {
            throw new ArgumentException($"Secret values must be {MaximumCredentialBlobSize} bytes or fewer.", nameof(value));
        }

        var blob = IntPtr.Zero;
        try
        {
            if (bytes.Length > 0)
            {
                blob = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
            }

            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = TargetPrefix + name,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = name
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                ZeroMemory(blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }

            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ZeroMemory(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }

    public bool Remove(string name)
    {
        if (NativeMethods.CredDelete(TargetPrefix + name, GenericCredential, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDelete(string targetName, uint type, uint flags);

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredEnumerate(string? filter, uint flags, out uint count, out IntPtr credentials);

        [DllImport("Advapi32.dll")]
        public static extern void CredFree(IntPtr buffer);
    }
}

