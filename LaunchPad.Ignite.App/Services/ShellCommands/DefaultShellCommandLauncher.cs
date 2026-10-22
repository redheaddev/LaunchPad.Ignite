using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.ShellCommands;

internal sealed class DefaultShellCommandLauncher : IPlatformShellCommandLauncher
{
    public ProcessStartInfo CreateStartInfo(string target, string? workingDirectory, IReadOnlyList<string> arguments, CommandLaunchMode launchMode)
    {
        var startInfo = new ProcessStartInfo(target);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (launchMode == CommandLaunchMode.Normal)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.UseShellExecute = true;
        startInfo.Verb = launchMode switch
        {
            CommandLaunchMode.Elevated => "runas",
            CommandLaunchMode.DifferentUser => "runasuser",
            _ => throw new ArgumentOutOfRangeException(nameof(launchMode))
        };
        startInfo.Arguments = string.Join(" ", arguments.Select(ShellCommandArgumentQuoter.Quote));

        return startInfo;
    }
}
