using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LaunchPad.Ignite.App.Services.Platform.MacOS;

public sealed class MacOSDirectFilePathPlatform : IDirectFilePathPlatform
{
    public const int MaximumSuggestions = 50;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, bool> _directoryExists;

    public MacOSDirectFilePathPlatform(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null)
    {
        _enumerateEntries = enumerateEntries ?? Directory.EnumerateFileSystemEntries;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public bool IsDirectPath(string text)
        => !string.IsNullOrWhiteSpace(text)
            && string.Equals(text, text.Trim(), StringComparison.Ordinal)
            && text.StartsWith("/", StringComparison.Ordinal);

    public bool TryGetCompletionPath(string text, out string path)
    {
        path = text;
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            path = text[1..^1];
        }

        return IsDirectPath(path);
    }

    public IReadOnlyList<string> GetSuggestions(string text)
    {
        if (!TryGetCompletionPath(text, out var path))
        {
            return Array.Empty<string>();
        }

        var separatorIndex = path.LastIndexOf('/');
        var directory = path[..(separatorIndex + 1)];
        var prefix = path[(separatorIndex + 1)..];
        try
        {
            return _enumerateEntries(directory)
                .Where(entry => GetName(entry).StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => (Path: entry, IsDirectory: _directoryExists(entry)))
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => GetName(entry.Path), StringComparer.Ordinal)
                .Take(MaximumSuggestions)
                .Select(entry => entry.IsDirectory ? EnsureTrailingSeparator(entry.Path) : entry.Path)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public bool TryAcceptNextPathSegment(string currentPath, string suggestion, out string acceptedPath)
    {
        acceptedPath = currentPath;
        if (!suggestion.StartsWith(currentPath, StringComparison.Ordinal)
            || suggestion.Length == currentPath.Length)
        {
            return false;
        }

        var remaining = suggestion[currentPath.Length..];
        var separatorStart = remaining.StartsWith("/", StringComparison.Ordinal) ? 1 : 0;
        var nextSeparatorIndex = remaining.IndexOf('/', separatorStart);
        var acceptedLength = nextSeparatorIndex < 0 ? remaining.Length : nextSeparatorIndex + 1;
        if (acceptedLength == 0)
        {
            return false;
        }

        acceptedPath = currentPath + remaining[..acceptedLength];
        return true;
    }

    public ProcessStartInfo CreateOpenPathStartInfo(string path, bool isDirectory)
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

    public bool IsPathPrefix(string path, string prefix)
        => path.StartsWith(prefix, StringComparison.Ordinal);

    private static string GetName(string path) => Path.GetFileName(path);

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith('/') ? path : path + '/';
}
