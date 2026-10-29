using System;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Services.Secrets.Linux;
using LaunchPad.Ignite.App.Services.Secrets.MacOS;
using LaunchPad.Ignite.App.Services.Secrets.Windows;

namespace LaunchPad.Ignite.App.Services.Secrets;

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

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceStore();
        }

        return new UnavailableSecretStore("Secret storage is not supported on this platform.");
    }
}

internal sealed class UnavailableSecretStore(string reason) : ISecretStore
{
    public bool IsAvailable => false;

    public string? UnavailableReason => reason;

    public IReadOnlyList<string> GetNames() => Array.Empty<string>();

    public bool TryGet(string name, out string value)
    {
        value = string.Empty;
        return false;
    }

    public void Set(string name, string value)
        => throw new PlatformNotSupportedException(reason);

    public bool Remove(string name) => false;
}

