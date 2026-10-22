using System.Collections.Generic;
using System.Diagnostics;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.Platform.ShellCommands;

internal interface IPlatformShellCommandLauncher
{
    ProcessStartInfo CreateStartInfo(string target, string? workingDirectory, IReadOnlyList<string> arguments, CommandLaunchMode launchMode);
}
