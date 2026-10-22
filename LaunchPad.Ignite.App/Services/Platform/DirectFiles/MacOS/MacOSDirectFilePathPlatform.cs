using System;
using System.Collections.Generic;
using System.Diagnostics;
using LaunchPad.Ignite.App.Services.Platform.DirectFiles.Posix;

namespace LaunchPad.Ignite.App.Services.Platform.DirectFiles.MacOS;

public sealed class MacOSDirectFilePathPlatform : PosixDirectFilePathPlatform
{
    public MacOSDirectFilePathPlatform(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null)
        : base(enumerateEntries, directoryExists)
    {
    }

    public override ProcessStartInfo CreateOpenPathStartInfo(string path, bool isDirectory)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false
        };
        if (!isDirectory)
        {
            startInfo.ArgumentList.Add("-R");
        }

        startInfo.ArgumentList.Add(path);
        return startInfo;
    }
}
