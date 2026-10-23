using System;
using LaunchPad.Ignite.App.Services.WindowBehavior.Linux;
using LaunchPad.Ignite.App.Services.WindowBehavior.Windows;

namespace LaunchPad.Ignite.App.Services.WindowBehavior;

internal static class WindowPlatformBehaviorFactory
{
    public static IWindowPlatformBehavior Create()
        => OperatingSystem.IsWindows()
            ? new WindowsWindowPlatformBehavior()
            : OperatingSystem.IsLinux()
                ? new LinuxWindowPlatformBehavior()
                : new DefaultWindowPlatformBehavior();
}
