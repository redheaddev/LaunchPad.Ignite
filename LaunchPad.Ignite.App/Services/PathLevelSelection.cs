using System;
using System.Collections.Generic;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Services;

public static class PathLevelSelection
{
    public static bool IsPathSelection(string inputText, int selectionStart, int selectionEnd)
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        var tokens = new CommandLineTokenizer().Tokenize(inputText);
        foreach (var token in tokens)
        {
            var rawToken = inputText.Substring(token.Start, token.Length);
            var equalsIndex = token.Value.IndexOf('=');
            var value = equalsIndex >= 0 ? token.Value[(equalsIndex + 1)..] : token.Value;
            if (!IsAbsolutePath(value))
            {
                continue;
            }

            var valueOffset = equalsIndex >= 0
                ? rawToken.IndexOf(value, rawToken.IndexOf('=') + 1, StringComparison.Ordinal)
                : rawToken.IndexOf(value, StringComparison.Ordinal);
            if (valueOffset < 0)
            {
                continue;
            }

            var pathStart = token.Start + valueOffset;
            var pathEnd = pathStart + value.Length;
            if (start >= pathStart && end <= pathEnd)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryAdjustSelection(
        string inputText,
        int caretIndex,
        int selectionStart,
        int selectionEnd,
        bool moveRight,
        out int adjustedStart,
        out int adjustedEnd,
        out int nextCaretIndex)
    {
        var normalizedSelectionStart = Math.Min(selectionStart, selectionEnd);
        var normalizedSelectionEnd = Math.Max(selectionStart, selectionEnd);
        adjustedStart = normalizedSelectionStart;
        adjustedEnd = normalizedSelectionEnd;
        nextCaretIndex = caretIndex;
        var hasSelection = normalizedSelectionStart < normalizedSelectionEnd;
        if (hasSelection)
        {
            if (moveRight)
            {
                if (!TryGetBoundary(inputText, normalizedSelectionStart, moveRight: true, out var nextStart))
                {
                    return false;
                }

                adjustedStart = Math.Min(nextStart, normalizedSelectionEnd);
                nextCaretIndex = adjustedStart;
                return true;
            }

            if (!TryGetBoundary(inputText, normalizedSelectionEnd, moveRight: false, out var previousEnd))
            {
                return false;
            }

            adjustedEnd = Math.Max(previousEnd, normalizedSelectionStart);
            nextCaretIndex = adjustedEnd;
            return true;
        }

        if (!TryGetBoundary(inputText, caretIndex, moveRight, out var boundary))
        {
            return false;
        }

        adjustedStart = Math.Min(caretIndex, boundary);
        adjustedEnd = Math.Max(caretIndex, boundary);
        nextCaretIndex = boundary;
        return true;
    }

    public static bool TryGetBoundary(string inputText, int caretIndex, bool moveRight, out int boundary)
    {
        boundary = caretIndex;
        var foundPath = false;
        var tokens = new CommandLineTokenizer().Tokenize(inputText);
        foreach (var token in tokens)
        {
            var rawToken = inputText.Substring(token.Start, token.Length);
            var equalsIndex = token.Value.IndexOf('=');
            var value = equalsIndex >= 0 ? token.Value[(equalsIndex + 1)..] : token.Value;
            if (!IsAbsolutePath(value))
            {
                continue;
            }

            var valueOffset = equalsIndex >= 0
                ? rawToken.IndexOf(value, rawToken.IndexOf('=') + 1, StringComparison.Ordinal)
                : rawToken.IndexOf(value, StringComparison.Ordinal);
            if (valueOffset < 0)
            {
                continue;
            }

            var pathStart = token.Start + valueOffset;
            var pathEnd = pathStart + value.Length;
            if (caretIndex < pathStart || caretIndex > pathEnd)
            {
                continue;
            }

            foundPath = true;
            boundary = moveRight
                ? GetNextBoundary(value, pathStart, caretIndex)
                : GetPreviousBoundary(value, pathStart, caretIndex);
            if (boundary != caretIndex)
            {
                return true;
            }
        }

        if (foundPath)
        {
            return false;
        }

        boundary = moveRight
            ? GetNextCommandBoundary(inputText, caretIndex)
            : GetPreviousCommandBoundary(inputText, caretIndex);
        return boundary != caretIndex;
    }

    private static int GetNextBoundary(string path, int pathStart, int caretIndex)
    {
        var pathEnd = pathStart + path.Length;
        var relativeCaret = caretIndex - pathStart;
        var nextSeparatorStart = relativeCaret;
        if (path.StartsWith("\\\\", StringComparison.Ordinal) && nextSeparatorStart < 2)
        {
            nextSeparatorStart = 2;
        }
        else if (nextSeparatorStart < path.Length && IsSeparator(path[nextSeparatorStart]))
        {
            nextSeparatorStart++;
        }

        for (var index = nextSeparatorStart; index < path.Length; index++)
        {
            if (IsSeparator(path[index]))
            {
                return pathStart + index;
            }
        }

        return pathEnd;
    }

    private static int GetPreviousBoundary(string path, int pathStart, int caretIndex)
    {
        var relativeCaret = caretIndex - pathStart;
        for (var index = relativeCaret - 1; index >= 0; index--)
        {
            if (!IsSeparator(path[index]))
            {
                continue;
            }

            if (index + 1 == relativeCaret)
            {
                for (var previousIndex = index - 1; previousIndex >= 0; previousIndex--)
                {
                    if (IsSeparator(path[previousIndex]))
                    {
                        return pathStart + previousIndex + 1;
                    }
                }

                return pathStart;
            }

            return pathStart + index + 1;
        }

        return pathStart;
    }

    private static int GetNextCommandBoundary(string inputText, int caretIndex)
    {
        var index = caretIndex;
        if (index < inputText.Length && IsCommandDelimiter(inputText[index]))
        {
            index++;
        }

        while (index < inputText.Length && !IsCommandDelimiter(inputText[index]))
        {
            index++;
        }

        return index;
    }

    private static int GetPreviousCommandBoundary(string inputText, int caretIndex)
    {
        var index = caretIndex;
        if (index > 0 && IsCommandDelimiter(inputText[index - 1]))
        {
            index--;
        }

        while (index > 0 && !IsCommandDelimiter(inputText[index - 1]))
        {
            index--;
        }

        return index;
    }

    private static bool IsCommandDelimiter(char value)
        => char.IsWhiteSpace(value) || value is '"' or ',';

    private static bool IsAbsolutePath(string value)
        => value.StartsWith("\\\\", StringComparison.Ordinal)
            || value.Length >= 3
                && char.IsAsciiLetter(value[0])
                && value[1] == ':'
                && IsSeparator(value[2]);

    private static bool IsSeparator(char value) => value is '\\' or '/';
}
