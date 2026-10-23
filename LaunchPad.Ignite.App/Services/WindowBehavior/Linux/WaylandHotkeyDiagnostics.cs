using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal static class WaylandHotkeyDiagnostics
{
    private const long MaximumLogSizeBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string LogPath => GetLogPath(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static void Write(string message, Exception? exception = null)
    {
        var entry = $"{DateTimeOffset.UtcNow:O} {message}";
        if (exception is not null)
        {
            entry += Environment.NewLine + exception;
        }

        try
        {
            var path = LogPath;
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The activation hotkey log directory could not be determined.");

            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length > MaximumLogSizeBytes)
                {
                    File.WriteAllText(path, string.Empty);
                }

                File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
                SetPrivatePermissions(directory, path);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceWarning($"Unable to write Wayland activation diagnostics: {loggingException.Message}");
        }
    }

    internal static string GetLogPath(string? xdgStateHome, string? userProfile)
    {
        var stateHome = !string.IsNullOrWhiteSpace(xdgStateHome) && Path.IsPathFullyQualified(xdgStateHome)
            ? xdgStateHome
            : Path.Combine(
                string.IsNullOrWhiteSpace(userProfile) ? Environment.CurrentDirectory : userProfile,
                ".local",
                "state");

        return Path.Combine(stateHome, "LaunchPad.Ignite", "activation-hotkey.log");
    }

    private static void SetPrivatePermissions(string directory, string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Trace.TraceWarning($"Unable to restrict permissions on the Wayland activation diagnostics file: {exception.Message}");
        }
    }
}
