using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LaunchPad.Ignite.App.Services.Platform.Windows;

public sealed class WindowsDirectFilePathPlatform : IDirectFilePathPlatform
{
    public const int MaximumSuggestions = 50;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, IEnumerable<string>> _enumerateUncShares;

    public WindowsDirectFilePathPlatform(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null,
        Func<string, IEnumerable<string>>? enumerateUncShares = null)
    {
        _enumerateEntries = enumerateEntries ?? Directory.EnumerateFileSystemEntries;
        _directoryExists = directoryExists ?? Directory.Exists;
        _enumerateUncShares = enumerateUncShares ?? WindowsUncShareEnumerator.EnumerateShares;
    }

    public bool IsDirectPath(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return text.Length >= 3
            && char.IsAsciiLetter(text[0])
            && text[1] == ':'
            && IsDirectorySeparator(text[2])
            || IsUncPath(text);
    }

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

        if (TryGetUncShareSuggestions(path, out var shareSuggestions))
        {
            return shareSuggestions;
        }

        var separatorIndex = path.LastIndexOfAny(['\\', '/']);
        if (separatorIndex < 0)
        {
            return Array.Empty<string>();
        }

        var directory = path[..(separatorIndex + 1)];
        var prefix = path[(separatorIndex + 1)..];
        try
        {
            return _enumerateEntries(directory)
                .Where(entry => GetName(entry).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Path: entry, IsDirectory: _directoryExists(entry)))
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => GetName(entry.Path), StringComparer.OrdinalIgnoreCase)
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
        if (!suggestion.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)
            || suggestion.Length == currentPath.Length)
        {
            return false;
        }

        var remaining = suggestion[currentPath.Length..];
        if (currentPath.Length == 1
            && char.IsAsciiLetter(currentPath[0])
            && remaining.Length >= 2
            && remaining[0] == ':'
            && IsDirectorySeparator(remaining[1]))
        {
            acceptedPath = currentPath + remaining[..2];
            return true;
        }

        if (currentPath.Length == 2
            && char.IsAsciiLetter(currentPath[0])
            && currentPath[1] == ':'
            && remaining.Length > 0
            && IsDirectorySeparator(remaining[0]))
        {
            acceptedPath = currentPath + remaining[0];
            return true;
        }

        var separatorStart = IsDirectorySeparator(remaining[0]) ? 1 : 0;
        var nextSeparatorIndex = remaining.IndexOfAny(['\\', '/'], separatorStart);
        var acceptedLength = nextSeparatorIndex < 0 ? remaining.Length : nextSeparatorIndex + 1;
        acceptedPath = currentPath + remaining[..acceptedLength];
        return acceptedLength > 0;
    }

    public ProcessStartInfo CreateOpenPathStartInfo(string path, bool isDirectory)
    {
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(isDirectory ? path : $"/select,{path}");
        return startInfo;
    }

    public bool IsPathPrefix(string path, string prefix)
        => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private bool TryGetUncShareSuggestions(string path, out IReadOnlyList<string> suggestions)
    {
        suggestions = Array.Empty<string>();
        if (!path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        var serverSeparatorIndex = path.IndexOf('\\', 2);
        if (serverSeparatorIndex < 0 || path.IndexOf('\\', serverSeparatorIndex + 1) >= 0)
        {
            return false;
        }

        var server = path[..serverSeparatorIndex];
        var sharePrefix = path[(serverSeparatorIndex + 1)..];
        try
        {
            suggestions = _enumerateUncShares(server)
                .Where(share => share.StartsWith(sharePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(share => share, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumSuggestions)
                .Select(share => $"{server}\\{share}\\")
                .ToArray();
        }
        catch (IOException)
        {
            suggestions = Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            suggestions = Array.Empty<string>();
        }
        catch (ArgumentException)
        {
            suggestions = Array.Empty<string>();
        }

        return true;
    }

    private static bool IsUncPath(string text)
    {
        if (!text.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        var serverEnd = text.IndexOf('\\', 2);
        return serverEnd > 2 || serverEnd < 0 && text.Length > 2;
    }

    private static string GetName(string path)
    {
        var separatorIndex = path.LastIndexOfAny(['\\', '/']);
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith('\\') || path.EndsWith('/') ? path : path + '\\';

    private static bool IsDirectorySeparator(char value) => value is '\\' or '/';
}
