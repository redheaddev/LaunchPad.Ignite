using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services;

public sealed class SystemUrlTargetOpener : IUrlTargetOpener
{
    public void Open(IEnumerable<UrlTarget> targets, bool newWindow = false)
    {
        foreach (var target in targets)
        {
            if (!newWindow || !TryOpenInNewBrowserWindow(target.Value))
            {
                Process.Start(new ProcessStartInfo(target.Value)
                {
                    UseShellExecute = true
                });
            }
        }
    }

    private static bool TryOpenInNewBrowserWindow(string url)
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
            startInfo.ArgumentList.Add(url);
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
