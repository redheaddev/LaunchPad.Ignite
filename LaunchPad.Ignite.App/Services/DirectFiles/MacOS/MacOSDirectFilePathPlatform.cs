using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LaunchPad.Ignite.App.Services.DirectFiles.Posix;

namespace LaunchPad.Ignite.App.Services.DirectFiles.MacOS;

public sealed class MacOSDirectFilePathPlatform : PosixDirectFilePathPlatform
{
    public MacOSDirectFilePathPlatform(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null,
        Func<string?>? pathProvider = null,
        Func<string, bool>? isExecutableFile = null)
        : base(enumerateEntries, directoryExists, pathProvider, isExecutableFile)
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

    public override bool TryCreateLaunchApplicationStartInfo(
        string path,
        bool isDirectory,
        IReadOnlyList<string> arguments,
        out ProcessStartInfo? startInfo)
    {
        if (isDirectory && string.Equals(Path.GetExtension(path), ".app", StringComparison.OrdinalIgnoreCase))
        {
            startInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(path);
            if (arguments.Count > 0)
            {
                startInfo.ArgumentList.Add("--args");
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            return true;
        }

        return base.TryCreateLaunchApplicationStartInfo(path, isDirectory, arguments, out startInfo);
    }
}
