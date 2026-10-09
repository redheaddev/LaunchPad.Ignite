using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.Tests;

public class PrintValueCommandParserTests
{
    [Theory]
    [InlineData("? x", "x")]
    [InlineData("? $project", "$project")]
    [InlineData("? @weather:temp(65738)", "@weather:temp(65738)")]
    [InlineData("?  x", "x")]
    public void TryParseCapturesExpression(string input, string expectedExpression)
    {
        Assert.True(PrintValueCommandParser.TryParse(input, out var expression));
        Assert.Equal(expectedExpression, expression);
    }

    [Theory]
    [InlineData("?x")]
    [InlineData("open value")]
    [InlineData("")]
    public void TryParseRejectsTextWithoutStandalonePrefix(string input)
    {
        Assert.False(PrintValueCommandParser.TryParse(input, out var expression));
        Assert.Empty(expression);
    }

    [Fact]
    public void TryParseRecognizesEmptyPrefixForValidationByCaller()
    {
        Assert.True(PrintValueCommandParser.TryParse("?", out var expression));
        Assert.Empty(expression);
    }
}
