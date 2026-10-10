using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LaunchPad.Ignite.App.Services;

public sealed class DirectFilePathCompletion
{
    public const int MaximumSuggestions = 50;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, bool> _directoryExists;

    public DirectFilePathCompletion(
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, bool>? directoryExists = null)
    {
        _enumerateEntries = enumerateEntries ?? Directory.EnumerateFileSystemEntries;
        _directoryExists = directoryExists ?? Directory.Exists;
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
            || text.StartsWith("\\\\", StringComparison.Ordinal);
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
                .Where(path => GetName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(path => (Path: path, IsDirectory: _directoryExists(path)))
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

    public static bool TrySelectSuggestion(IReadOnlyList<string> suggestions, int index, out string suggestion)
    {
        if (index < 0 || index >= suggestions.Count)
        {
            suggestion = string.Empty;
            return false;
        }

        suggestion = suggestions[index];
        return true;
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
