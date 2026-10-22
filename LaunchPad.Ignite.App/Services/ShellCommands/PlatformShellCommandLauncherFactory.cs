using System;
using LaunchPad.Ignite.App.Services.ShellCommands.Windows;

namespace LaunchPad.Ignite.App.Services.ShellCommands;

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
