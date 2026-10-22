using System;
using LaunchPad.Ignite.App.Services.Platform.ShellCommands.Windows;

namespace LaunchPad.Ignite.App.Services.Platform.ShellCommands;

internal static class PlatformShellCommandLauncherFactory
{
    public static IPlatformShellCommandLauncher Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsShellCommandLauncher();
        }

        return new DefaultShellCommandLauncher();
    }
}
