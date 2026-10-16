namespace LaunchPad.Ignite.Core.Parsing;

public sealed record CommandLineToken(string Value, int Start, int Length);

public sealed class CommandLineTokenizer
{
    public IReadOnlyList<CommandLineToken> Tokenize(string input)
    {
        var tokens = new List<CommandLineToken>();
        var index = 0;

        while (index < input.Length)
        {
            while (index < input.Length && char.IsWhiteSpace(input[index]))
            {
                index++;
            }

            if (index >= input.Length)
            {
                break;
            }

            var start = index;
            var value = new System.Text.StringBuilder();
            var insideQuotes = false;
            while (index < input.Length && (insideQuotes || !char.IsWhiteSpace(input[index])))
            {
                if (insideQuotes
                    && input[index] == '\\'
                    && index + 1 < input.Length
                    && input[index + 1] is '"' or '\\')
                {
                    value.Append(input[index + 1]);
                    index += 2;
                    continue;
                }

                if (input[index] == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else
                {
                    value.Append(input[index]);
                }

                index++;
            }

            tokens.Add(new CommandLineToken(value.ToString(), start, index - start));
        }

        return tokens;
    }

    public bool EndsWithWhitespace(string input)
        => input.Length > 0 && char.IsWhiteSpace(input[^1]);

    public int FindTokenBoundary(string input, int start, bool commaIsBoundary = true)
    {
        var insideQuotes = false;
        for (var index = start; index < input.Length; index++)
        {
            if (insideQuotes
                && input[index] == '\\'
                && index + 1 < input.Length
                && input[index + 1] is '"' or '\\')
            {
                index++;
            }
            else if (input[index] == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if (!insideQuotes && (char.IsWhiteSpace(input[index]) || (commaIsBoundary && input[index] == ',')))
            {
                return index;
            }
        }

        return input.Length;
    }
}
