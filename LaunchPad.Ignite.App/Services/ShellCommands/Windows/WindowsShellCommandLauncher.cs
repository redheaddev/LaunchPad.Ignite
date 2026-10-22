using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.App.Services.ShellCommands;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.ShellCommands.Windows;

internal sealed class WindowsShellCommandLauncher : IPlatformShellCommandLauncher
{
    public ProcessStartInfo CreateStartInfo(string target, string? workingDirectory, IReadOnlyList<string> arguments, CommandLaunchMode launchMode)
    {
        var startInfo = new ProcessStartInfo(target);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        var isShortcut = string.Equals(Path.GetExtension(target), ".lnk", System.StringComparison.OrdinalIgnoreCase);
        if (launchMode == CommandLaunchMode.Normal && !isShortcut)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.UseShellExecute = true;
        if (launchMode != CommandLaunchMode.Normal)
        {
            startInfo.Verb = launchMode switch
            {
                CommandLaunchMode.Elevated => "runas",
                CommandLaunchMode.DifferentUser => "runasuser",
                _ => throw new System.ArgumentOutOfRangeException(nameof(launchMode))
            };
        }

        startInfo.Arguments = string.Join(" ", arguments.Select(ShellCommandArgumentQuoter.Quote));
        return startInfo;
    }
}
