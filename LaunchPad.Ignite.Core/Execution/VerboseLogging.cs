using System;
using System.IO;
using System.Threading;

namespace LaunchPad.Ignite.Core.Execution;

public static class VerboseLogging
{
    private static int _enabled;

    public static bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    public static void Configure(bool enabled)
        => Volatile.Write(ref _enabled, enabled ? 1 : 0);

    public static void LogException(string source, string message, Exception exception)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite");
            Directory.CreateDirectory(profileDirectory);
            var path = Path.Combine(profileDirectory, "log.txt");
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} [VERBOSE] [{source}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
