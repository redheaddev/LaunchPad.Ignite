using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.App.Services.Terminals;

public sealed class SystemSecretTerminalLauncher : ISecretTerminalLauncher
{
    private static readonly UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly IProcessStarter _processStarter;

    public SystemSecretTerminalLauncher(IProcessStarter processStarter)
    {
        _processStarter = processStarter;
    }

    public void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory)
    {
        var directory = workingDirectory is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The requested working directory does not exist.");
        }

        if (OperatingSystem.IsWindows())
        {
            OpenWindowsShell(environment, directory);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            OpenMacShell(environment, directory);
            return;
        }

        throw new PlatformNotSupportedException("Secret terminal sessions are supported on Windows and macOS.");
    }

    public void RunScript(string scriptPath, string type, IReadOnlyDictionary<string, string> environment)
    {
        var fullScriptPath = Path.GetFullPath(scriptPath);
        if (!File.Exists(fullScriptPath))
        {
            throw new FileNotFoundException("Snippet script was not found.", fullScriptPath);
        }

        if (OperatingSystem.IsWindows())
        {
            if (!string.Equals(type, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlatformNotSupportedException("Shell snippets are supported only on macOS.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = FindExecutable("pwsh.exe") ?? "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(fullScriptPath);
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }

            _processStarter.Start(startInfo);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!string.Equals(type, "shell", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlatformNotSupportedException("PowerShell snippets are supported only on Windows.");
            }

            RunMacScript(fullScriptPath, environment);
            return;
        }

        throw new PlatformNotSupportedException("Snippet terminals are supported on Windows and macOS.");
    }

    private void OpenWindowsShell(IReadOnlyDictionary<string, string> environment, string directory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindExecutable("pwsh.exe") ?? "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = directory
        };
        startInfo.ArgumentList.Add("-NoExit");
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        _processStarter.Start(startInfo);
    }

    [SupportedOSPlatform("macos")]
    private void OpenMacShell(IReadOnlyDictionary<string, string> environment, string directory)
    {
        var scriptsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "terminal-sessions");
        Directory.CreateDirectory(scriptsDirectory);
        File.SetUnixFileMode(scriptsDirectory, PrivateDirectoryMode);
        foreach (var staleScript in Directory.EnumerateFiles(scriptsDirectory, "*.command"))
        {
            File.Delete(staleScript);
        }

        var scriptPath = Path.Combine(scriptsDirectory, $"{Guid.NewGuid():N}.command");
        var script = BuildMacSessionScript(environment, directory, scriptPath);
        try
        {
            using (var stream = new FileStream(scriptPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = PrivateFileMode
            }))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(script);
            }

            var shellCommand = $"/bin/zsh {QuoteShellArgument(scriptPath)}";
            var appleScript = $"tell application \"Terminal\" to do script {QuoteAppleScriptString(shellCommand)}";
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(appleScript);
            _processStarter.Start(startInfo);
        }
        catch
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }

            throw;
        }
        finally
        {
            script = string.Empty;
        }
    }

    private static string BuildMacSessionScript(
        IReadOnlyDictionary<string, string> environment,
        string directory,
        string scriptPath)
    {
        var builder = new StringBuilder("unset HISTFILE\n");
        foreach (var (name, value) in environment)
        {
            builder.Append("export ")
                .Append(name)
                .Append('=')
                .Append(QuoteShellArgument(value))
                .Append('\n');
        }

        builder.Append("cd -- ")
            .Append(QuoteShellArgument(directory))
            .Append(" || exit\nrm -f -- ")
            .Append(QuoteShellArgument(scriptPath))
            .Append("\nexec /bin/zsh -i\n");
        return builder.ToString();
    }

    [SupportedOSPlatform("macos")]
    private void RunMacScript(string snippetPath, IReadOnlyDictionary<string, string> environment)
    {
        var scriptsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "terminal-sessions");
        Directory.CreateDirectory(scriptsDirectory);
        File.SetUnixFileMode(scriptsDirectory, PrivateDirectoryMode);
        var sessionScriptPath = Path.Combine(scriptsDirectory, $"{Guid.NewGuid():N}.command");
        var builder = new StringBuilder("unset HISTFILE\n");
        foreach (var (name, value) in environment)
        {
            builder.Append("export ")
                .Append(name)
                .Append('=')
                .Append(QuoteShellArgument(value))
                .Append('\n');
        }

        builder.Append("cd -- ")
            .Append(QuoteShellArgument(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
            .Append(" || exit\nsource -- ")
            .Append(QuoteShellArgument(snippetPath))
            .Append("\nsnippet_status=$?\nrm -f -- ")
            .Append(QuoteShellArgument(sessionScriptPath))
            .Append("\nif [ \"$snippet_status\" -ne 0 ]; then echo \"Snippet exited with status $snippet_status\"; fi\nexec /bin/zsh -i\n");

        var script = builder.ToString();
        try
        {
            using (var stream = new FileStream(sessionScriptPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = PrivateFileMode
            }))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(script);
            }

            var shellCommand = $"/bin/zsh {QuoteShellArgument(sessionScriptPath)}";
            var appleScript = $"tell application \"Terminal\" to do script {QuoteAppleScriptString(shellCommand)}";
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(appleScript);
            _processStarter.Start(startInfo);
        }
        catch
        {
            if (File.Exists(sessionScriptPath))
            {
                File.Delete(sessionScriptPath);
            }

            throw;
        }
        finally
        {
            script = string.Empty;
        }
    }

    private static string? FindExecutable(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, executableName))
            .FirstOrDefault(File.Exists);
    }

    private static string QuoteShellArgument(string value)
        => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string QuoteAppleScriptString(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
