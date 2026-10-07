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
            while (index < input.Length && !char.IsWhiteSpace(input[index]))
            {
                index++;
            }

            tokens.Add(new CommandLineToken(input[start..index], start, index - start));
        }

        return tokens;
    }

    public bool EndsWithWhitespace(string input)
        => input.Length > 0 && char.IsWhiteSpace(input[^1]);
}
