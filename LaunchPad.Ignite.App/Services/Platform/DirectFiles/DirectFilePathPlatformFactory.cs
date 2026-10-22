using System;
using LaunchPad.Ignite.App.Services.Platform.DirectFiles.Linux;
using LaunchPad.Ignite.App.Services.Platform.DirectFiles.MacOS;
using LaunchPad.Ignite.App.Services.Platform.DirectFiles.Windows;

namespace LaunchPad.Ignite.App.Services.Platform.DirectFiles;

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

        if (OperatingSystem.IsLinux())
        {
            return new LinuxDirectFilePathPlatform();
        }

        throw new PlatformNotSupportedException("Direct file path completion is not supported on this platform.");
    }
}
