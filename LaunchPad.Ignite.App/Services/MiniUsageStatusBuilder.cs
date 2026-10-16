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
        bool hasSelectedCommandWithTrailingSpace)
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

        if (hasSelectedCommandWithTrailingSpace && !string.IsNullOrWhiteSpace(usageLine))
        {
            return usageLine;
        }

        var itemIndex = highlightedIndex;
        if (itemIndex < 0 && completionItems.Count > 0)
        {
            itemIndex = 0;
        }

        if (itemIndex >= 0 && itemIndex < completionItems.Count)
        {
            var itemText = completionItems[itemIndex];
            var status = string.IsNullOrWhiteSpace(usageLine) ? itemText : $"{itemText} · {usageLine}";
            return $"{status} · {itemIndex + 1}/{completionItems.Count}";
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
