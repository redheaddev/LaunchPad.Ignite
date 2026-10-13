using System;
using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.App.Services;

public static class SnippetCompletion
{
    public static bool TryGetFilterPrefix(string inputText, out string prefix)
    {
        var trimmedInput = inputText.Trim();
        if (!trimmedInput.StartsWith('^'))
        {
            prefix = string.Empty;
            return false;
        }

        prefix = trimmedInput[1..].Trim();
        return true;
    }

    public static IReadOnlyList<string> Filter(IEnumerable<string> names, string inputText)
    {
        if (!TryGetFilterPrefix(inputText, out var prefix))
        {
            return Array.Empty<string>();
        }

        return names
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string BuildRunCommand(string name) => $"snippet run {name}";
}
