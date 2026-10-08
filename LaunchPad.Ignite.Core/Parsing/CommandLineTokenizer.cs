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
}
