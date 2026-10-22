using System;
using LaunchPad.Ignite.App.Services.WindowBehavior.Windows;

namespace LaunchPad.Ignite.App.Services.WindowBehavior;

internal static class WindowPlatformBehaviorFactory
{
    public static IWindowPlatformBehavior Create()
        => OperatingSystem.IsWindows()
            ? new WindowsWindowPlatformBehavior()
            : new DefaultWindowPlatformBehavior();
}
