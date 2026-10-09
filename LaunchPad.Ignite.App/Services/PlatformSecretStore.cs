using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LaunchPad.Ignite.App.Services;

public static class PlatformSecretStore
{
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialSecretStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainSecretStore();
        }

        throw new PlatformNotSupportedException("Secrets are supported on Windows and macOS.");
    }
}

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

internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "LaunchPad.Ignite";
    private const uint GenericPasswordItemClass = 0x67656e70;
    private const uint AccountAttribute = 0x61636374;
    private const uint ServiceAttribute = 0x73766365;
    private const int ItemNotFound = -25300;

    public IReadOnlyList<string> GetNames()
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var servicePointer = Allocate(serviceBytes);
        var attribute = new KeychainAttribute
        {
            Tag = ServiceAttribute,
            Length = (uint)serviceBytes.Length,
            Data = servicePointer
        };
        var attributePointer = Marshal.AllocHGlobal(Marshal.SizeOf<KeychainAttribute>());
        Marshal.StructureToPtr(attribute, attributePointer, false);
        var attributeList = new KeychainAttributeList { Count = 1, Attributes = attributePointer };

        try
        {
            var status = NativeMethods.SecKeychainSearchCreateFromAttributes(
                IntPtr.Zero,
                GenericPasswordItemClass,
                ref attributeList,
                out var search);
            EnsureSuccess(status);
            try
            {
                var names = new List<string>();
                while (NativeMethods.SecKeychainSearchCopyNext(search, out var item) == 0)
                {
                    try
                    {
                        var name = ReadAccountName(item);
                        if (name is not null)
                        {
                            names.Add(name);
                        }
                    }
                    finally
                    {
                        NativeMethods.CFRelease(item);
                    }
                }

                return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            finally
            {
                NativeMethods.CFRelease(search);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(attributePointer);
            Marshal.FreeHGlobal(servicePointer);
            Array.Clear(serviceBytes);
        }
    }

    public bool TryGet(string name, out string value)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(name);
        var servicePointer = Allocate(serviceBytes);
        var accountPointer = Allocate(accountBytes);
        try
        {
            var status = NativeMethods.SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                servicePointer,
                (uint)accountBytes.Length,
                accountPointer,
                out var passwordLength,
                out var passwordData,
                out var item);
            if (status == ItemNotFound)
            {
                value = string.Empty;
                return false;
            }

            EnsureSuccess(status);
            try
            {
                var bytes = new byte[passwordLength];
                if (bytes.Length > 0)
                {
                    Marshal.Copy(passwordData, bytes, 0, bytes.Length);
                }

                value = Encoding.UTF8.GetString(bytes);
                CryptographicOperations.ZeroMemory(bytes);
                return true;
            }
            finally
            {
                NativeMethods.SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
                NativeMethods.CFRelease(item);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(accountPointer);
            Marshal.FreeHGlobal(servicePointer);
            Array.Clear(accountBytes);
            Array.Clear(serviceBytes);
        }
    }

    public void Set(string name, string value)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(name);
        var passwordBytes = Encoding.UTF8.GetBytes(value);
        var servicePointer = Allocate(serviceBytes);
        var accountPointer = Allocate(accountBytes);
        var passwordPointer = Allocate(passwordBytes);
        try
        {
            var status = NativeMethods.SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                servicePointer,
                (uint)accountBytes.Length,
                accountPointer,
                out _,
                out var oldPassword,
                out var item);
            if (status == 0)
            {
                NativeMethods.SecKeychainItemFreeContent(IntPtr.Zero, oldPassword);
                try
                {
                    EnsureSuccess(NativeMethods.SecKeychainItemModifyAttributesAndData(
                        item,
                        IntPtr.Zero,
                        (uint)passwordBytes.Length,
                        passwordPointer));
                }
                finally
                {
                    NativeMethods.CFRelease(item);
                }

                return;
            }

            if (status != ItemNotFound)
            {
                EnsureSuccess(status);
            }

            EnsureSuccess(NativeMethods.SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                servicePointer,
                (uint)accountBytes.Length,
                accountPointer,
                (uint)passwordBytes.Length,
                passwordPointer,
                out var newItem));
            if (newItem != IntPtr.Zero)
            {
                NativeMethods.CFRelease(newItem);
            }
        }
        finally
        {
            ZeroMemory(passwordPointer, passwordBytes.Length);
            Marshal.FreeHGlobal(passwordPointer);
            Marshal.FreeHGlobal(accountPointer);
            Marshal.FreeHGlobal(servicePointer);
            CryptographicOperations.ZeroMemory(passwordBytes);
            Array.Clear(accountBytes);
            Array.Clear(serviceBytes);
        }
    }

    public bool Remove(string name)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(name);
        var servicePointer = Allocate(serviceBytes);
        var accountPointer = Allocate(accountBytes);
        try
        {
            var status = NativeMethods.SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                servicePointer,
                (uint)accountBytes.Length,
                accountPointer,
                out _,
                out var passwordData,
                out var item);
            if (status == ItemNotFound)
            {
                return false;
            }

            EnsureSuccess(status);
            NativeMethods.SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            try
            {
                EnsureSuccess(NativeMethods.SecKeychainItemDelete(item));
                return true;
            }
            finally
            {
                NativeMethods.CFRelease(item);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(accountPointer);
            Marshal.FreeHGlobal(servicePointer);
            Array.Clear(accountBytes);
            Array.Clear(serviceBytes);
        }
    }

    private static string? ReadAccountName(IntPtr item)
    {
        var status = NativeMethods.SecKeychainItemCopyAttributesAndData(
            item,
            IntPtr.Zero,
            out _,
            out var attributeListPointer,
            out _,
            out var itemData);
        EnsureSuccess(status);
        try
        {
            var attributeList = Marshal.PtrToStructure<KeychainAttributeList>(attributeListPointer);
            for (var index = 0; index < attributeList.Count; index++)
            {
                var attributePointer = IntPtr.Add(
                    attributeList.Attributes,
                    checked((int)index * Marshal.SizeOf<KeychainAttribute>()));
                var attribute = Marshal.PtrToStructure<KeychainAttribute>(attributePointer);
                if (attribute.Tag == AccountAttribute)
                {
                    var bytes = new byte[attribute.Length];
                    Marshal.Copy(attribute.Data, bytes, 0, bytes.Length);
                    var name = Encoding.UTF8.GetString(bytes);
                    Array.Clear(bytes);
                    return name;
                }
            }

            return null;
        }
        finally
        {
            NativeMethods.SecKeychainItemFreeContent(attributeListPointer, itemData);
        }
    }

    private static IntPtr Allocate(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return IntPtr.Zero;
        }

        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static void ZeroMemory(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        for (var index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }

    private static void EnsureSuccess(int status)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"The macOS Keychain operation failed with status {status}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeychainAttribute
    {
        public uint Tag;
        public uint Length;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeychainAttributeList
    {
        public uint Count;
        public IntPtr Attributes;
    }

    private static class NativeMethods
    {
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            IntPtr serviceName,
            uint accountNameLength,
            IntPtr accountName,
            uint passwordLength,
            IntPtr passwordData,
            out IntPtr item);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainFindGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            IntPtr serviceName,
            uint accountNameLength,
            IntPtr accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr item);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemModifyAttributesAndData(
            IntPtr item,
            IntPtr attributes,
            uint length,
            IntPtr data);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemDelete(IntPtr item);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainSearchCreateFromAttributes(
            IntPtr keychain,
            uint itemClass,
            ref KeychainAttributeList attributes,
            out IntPtr search);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainSearchCopyNext(IntPtr search, out IntPtr item);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        public static extern int SecKeychainItemCopyAttributesAndData(
            IntPtr item,
            IntPtr info,
            out uint itemClass,
            out IntPtr attributes,
            out uint length,
            out IntPtr data);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        public static extern void CFRelease(IntPtr value);
    }
}
