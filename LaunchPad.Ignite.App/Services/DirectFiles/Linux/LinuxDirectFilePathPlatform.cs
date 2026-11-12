using System;
using System.Diagnostics;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Services.DirectFiles.Posix;

namespace LaunchPad.Ignite.App.Services.DirectFiles.Linux;

public sealed class LinuxDirectFilePathPlatform : PosixDirectFilePathPlatform
{
    public LinuxDirectFilePathPlatform(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null,
        Func<string?>? pathProvider = null,
        Func<string, bool>? isExecutableFile = null)
        : base(enumerateEntries, directoryExists, pathProvider, isExecutableFile)
    {
    }

    public override ProcessStartInfo CreateOpenPathStartInfo(string path, bool isDirectory)
    {
        var startInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(path);
        return startInfo;
    }
}
