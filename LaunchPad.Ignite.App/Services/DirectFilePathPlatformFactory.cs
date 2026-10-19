using System;
using LaunchPad.Ignite.App.Services.Platform.MacOS;
using LaunchPad.Ignite.App.Services.Platform.Windows;

namespace LaunchPad.Ignite.App.Services;

public static class DirectFilePathPlatformFactory
{
    public static IDirectFilePathPlatform Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDirectFilePathPlatform();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSDirectFilePathPlatform();
        }

        throw new PlatformNotSupportedException("Direct file path completion is supported on Windows and macOS.");
    }
}
