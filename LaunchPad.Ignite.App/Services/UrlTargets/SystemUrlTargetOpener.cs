using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.UrlTargets;

public sealed class SystemUrlTargetOpener : IUrlTargetOpener
{
    public void Open(IEnumerable<UrlTarget> targets, bool newWindow = false)
    {
        var targetList = targets.ToArray();
        if (targetList.Length == 0)
        {
            return;
        }

        if (newWindow && TryOpenInNewBrowserWindow(targetList))
        {
            return;
        }

        foreach (var target in targetList)
        {
            Process.Start(new ProcessStartInfo(target.Value)
            {
                UseShellExecute = true
            });
        }
    }

    private static bool TryOpenInNewBrowserWindow(IReadOnlyList<UrlTarget> targets)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var userChoice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var programId = userChoice?.GetValue("ProgId") as string;
            if (string.IsNullOrWhiteSpace(programId))
            {
                return false;
            }

            using var openCommand = Registry.ClassesRoot.OpenSubKey($@"{programId}\shell\open\command");
            var command = openCommand?.GetValue(null) as string;
            var executable = GetExecutable(command);
            if (executable is null)
            {
                return false;
            }

            var browserName = Path.GetFileNameWithoutExtension(executable);
            var newWindowArgument = browserName.Contains("firefox", StringComparison.OrdinalIgnoreCase)
                ? "-new-window"
                : browserName.Contains("chrome", StringComparison.OrdinalIgnoreCase)
                    || browserName.Contains("edge", StringComparison.OrdinalIgnoreCase)
                    || browserName.Contains("brave", StringComparison.OrdinalIgnoreCase)
                    || browserName.Contains("vivaldi", StringComparison.OrdinalIgnoreCase)
                    || browserName.Contains("opera", StringComparison.OrdinalIgnoreCase)
                    ? "--new-window"
                    : null;
            if (newWindowArgument is null)
            {
                return false;
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(newWindowArgument);
            foreach (var target in targets)
            {
                startInfo.ArgumentList.Add(target.Value);
            }

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        command = command.TrimStart();
        if (command.StartsWith('"'))
        {
            var closingQuote = command.IndexOf('"', 1);
            return closingQuote > 1 ? command[1..closingQuote] : null;
        }

        var separator = command.IndexOf(' ');
        return separator > 0 ? command[..separator] : command;
    }
}
