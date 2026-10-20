using System;

namespace LaunchPad.Ignite.App.Services;

public static class CompletionText
{
    public static string GetGhostSuffix(string candidate, string prefix)
        => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate[prefix.Length..]
            : string.Empty;
}
