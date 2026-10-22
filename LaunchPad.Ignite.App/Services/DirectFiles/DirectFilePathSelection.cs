using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services.DirectFiles;

public static class DirectFilePathSelection
{
    public static bool TrySelectSuggestion<T>(IReadOnlyList<T> suggestions, int index, out T suggestion)
    {
        if (index < 0 || index >= suggestions.Count)
        {
            suggestion = default!;
            return false;
        }

        suggestion = suggestions[index];
        return true;
    }

    public static bool TryGetPath(string text, int selectionStart, int selectionEnd, int caretIndex, out string path)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        if (start < end)
        {
            path = text[start..end].Trim();
            return path.Length > 0;
        }

        var caret = Math.Clamp(caretIndex, 0, text.Length);
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var tokenStart = index;
            var insideQuotes = false;
            while (index < text.Length)
            {
                if (text[index] == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else if (char.IsWhiteSpace(text[index]) && !insideQuotes)
                {
                    break;
                }

                index++;
            }

            if (caret >= tokenStart && caret <= index)
            {
                path = text[tokenStart..index];
                return path.Length > 0;
            }
        }

        path = string.Empty;
        return false;
    }
}
