using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LaunchPad.Ignite.App.Services.DirectFiles.Windows;

internal static class WindowsUncShareEnumerator
{
    private const int MaximumPreferredLength = -1;
    private const int ErrorMoreData = 234;
    private const uint DiskTreeShare = 0;

    public static IReadOnlyList<string> EnumerateShares(string serverName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            var status = NetShareEnum(
                serverName,
                1,
                out buffer,
                MaximumPreferredLength,
                out var entriesRead,
                out _,
                out _);
            if (status != 0 && status != ErrorMoreData)
            {
                return Array.Empty<string>();
            }

            var shares = new List<string>(entriesRead);
            var entrySize = Marshal.SizeOf<ShareInfo1>();
            for (var index = 0; index < entriesRead; index++)
            {
                var entry = Marshal.PtrToStructure<ShareInfo1>(IntPtr.Add(buffer, index * entrySize));
                if (entry.Type == DiskTreeShare && !string.IsNullOrEmpty(entry.Name))
                {
                    shares.Add(entry.Name);
                }
            }

            return shares;
        }
        catch (DllNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (EntryPointNotFoundException)
        {
            return Array.Empty<string>();
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _ = NetApiBufferFree(buffer);
            }
        }
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(
        string serverName,
        int level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        out int resumeHandle);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Name;

        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Remark;
    }
}
