namespace LaunchPad.Ignite.Core.Completion;

public static class CommandCompletionSectionParser
{
    public static int FindNextSectionEnd(string text, int startIndex)
    {
        startIndex = Math.Clamp(startIndex, 0, text.Length);
        var quote = '\0';
        for (var index = 0; index < startIndex; index++)
        {
            UpdateQuoteState(text, index, ref quote);
        }

        for (var index = startIndex; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote && !IsEscaped(text, index))
                {
                    quote = '\0';
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character is '/' or '\\' || char.IsWhiteSpace(character))
            {
                return index + 1;
            }
        }

        return text.Length;
    }

    public static int FindPreviousSectionStart(string text, int caretIndex)
    {
        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var sectionStart = 0;
        while (sectionStart < caretIndex)
        {
            var sectionEnd = FindNextSectionEnd(text, sectionStart);
            if (sectionEnd >= caretIndex)
            {
                return sectionStart;
            }

            sectionStart = sectionEnd;
        }

        return sectionStart;
    }

    private static void UpdateQuoteState(string text, int index, ref char quote)
    {
        var character = text[index];
        if (quote == '\0')
        {
            if (character is '"' or '\'')
            {
                quote = character;
            }
        }
        else if (character == quote && !IsEscaped(text, index))
        {
            quote = '\0';
        }
    }

    private static bool IsEscaped(string text, int index)
    {
        var precedingBackslashes = 0;
        for (var previousIndex = index - 1; previousIndex >= 0 && text[previousIndex] == '\\'; previousIndex--)
        {
            precedingBackslashes++;
        }

        return precedingBackslashes % 2 != 0;
    }
}
