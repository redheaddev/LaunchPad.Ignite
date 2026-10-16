using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.Tests;

public class CommandLineTokenizerTests
{
    private readonly CommandLineTokenizer _tokenizer = new();

    [Fact]
    public void TokenizeGroupsQuotedWhitespaceAndPreservesSourceSpans()
    {
        const string input = "run \"C:\\Program Files\\App.exe\" \"two words\"";

        var tokens = _tokenizer.Tokenize(input);

        Assert.Equal(["run", "C:\\Program Files\\App.exe", "two words"], tokens.Select(token => token.Value));
        Assert.Equal(input.IndexOf('"'), tokens[1].Start);
        Assert.Equal("\"C:\\Program Files\\App.exe\"".Length, tokens[1].Length);
    }

    [Fact]
    public void TokenizeAcceptsAnUnclosedQuoteAsAnIncompleteFinalToken()
    {
        var tokens = _tokenizer.Tokenize("run \"two words");

        Assert.Equal("two words", tokens[1].Value);
    }

    [Fact]
    public void TokenizeUnescapesQuotedQuotesAndBackslashes()
    {
        var tokens = _tokenizer.Tokenize("run \"a \\\"quoted\\\" value\\\\path\"");

        Assert.Equal("a \"quoted\" value\\path", tokens[1].Value);
    }

    [Fact]
    public void FindTokenBoundaryKeepsQuotedSpacesAndCommaInsideValue()
    {
        const string input = "\"value, value value\"] next";

        var boundary = _tokenizer.FindTokenBoundary(input, 0);

        Assert.Equal(input.IndexOf(" next", StringComparison.Ordinal), boundary);
    }
}
