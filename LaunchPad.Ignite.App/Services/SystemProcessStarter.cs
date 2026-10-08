using System;
using System.Diagnostics;

namespace LaunchPad.Ignite.App.Services;

public sealed class SystemProcessStarter : IProcessStarter
{
    public void Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start process '{startInfo.FileName}'.");
    }
}
