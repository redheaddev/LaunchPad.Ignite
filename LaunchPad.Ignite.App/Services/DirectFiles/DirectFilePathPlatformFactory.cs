using System;
using LaunchPad.Ignite.App.Services.DirectFiles.Linux;
using LaunchPad.Ignite.App.Services.DirectFiles.MacOS;
using LaunchPad.Ignite.App.Services.DirectFiles.Windows;

namespace LaunchPad.Ignite.App.Services.DirectFiles;

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
