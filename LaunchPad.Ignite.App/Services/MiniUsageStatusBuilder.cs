using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public static class MiniUsageStatusBuilder
{
    public static string Build(
        string commandInputText,
        string usageText,
        IReadOnlyList<string> completionItems,
        int highlightedIndex,
        bool hasSelectedCommandWithTrailingSpace,
        int positionIndex = -1,
        int positionCount = 0)
    {
        string? usageLine = null;
        foreach (var line in usageText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            usageLine = line;
            break;
        }
        if (usageLine is not null && !usageLine.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
        {
            usageLine = null;
        }

        var itemIndex = highlightedIndex;
        if (itemIndex < 0 && completionItems.Count > 0)
        {
            itemIndex = 0;
        }

        var usesHistoryPosition = positionIndex >= 0 && positionCount > 0;
        var displayIndex = usesHistoryPosition ? positionIndex : itemIndex;
        var displayCount = usesHistoryPosition ? positionCount : completionItems.Count;

        if (hasSelectedCommandWithTrailingSpace && !string.IsNullOrWhiteSpace(usageLine))
        {
            if (itemIndex >= 0 && itemIndex < completionItems.Count && displayIndex >= 0 && displayCount > 0)
            {
                var status = completionItems[itemIndex].StartsWith("↶ ", StringComparison.Ordinal)
                    ? $"{completionItems[itemIndex]} · {usageLine}"
                    : usageLine;
                return $"{displayIndex + 1}/{displayCount} · {status}";
            }

            return usageLine;
        }

        if (itemIndex >= 0 && itemIndex < completionItems.Count)
        {
            var itemText = completionItems[itemIndex];
            var status = string.IsNullOrWhiteSpace(usageLine) ? itemText : $"{itemText} · {usageLine}";
            return $"{displayIndex + 1}/{displayCount} · {status}";
        }

        if (!string.IsNullOrWhiteSpace(usageLine))
        {
            return usageLine;
        }

        return string.IsNullOrWhiteSpace(commandInputText)
            ? "Start typing to see commands"
            : "No matches";
    }
}
