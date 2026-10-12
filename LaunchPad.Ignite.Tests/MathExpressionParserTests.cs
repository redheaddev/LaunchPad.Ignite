using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.Tests;

public class MathExpressionParserTests
{
    [Theory]
    [InlineData("=2 + 3", "2 + 3")]
    [InlineData("=  (2 + 3)", "(2 + 3)")]
    [InlineData("=", "")]
    public void TryParseRecognizesMathPrefix(string input, string expectedExpression)
    {
        Assert.True(MathExpressionParser.TryParse(input, out var expression));
        Assert.Equal(expectedExpression, expression);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2+3")]
    [InlineData(" ? 2+3")]
    public void TryParseRejectsInputWithoutMathPrefix(string input)
    {
        Assert.False(MathExpressionParser.TryParse(input, out var expression));
        Assert.Empty(expression);
    }

    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("-2^2", -4)]
    [InlineData("2^3^2", 512)]
    [InlineData("2^-2", 0.25)]
    [InlineData("10 % 3", 1)]
    [InlineData("1.5e2 + .5", 150.5)]
    public void TryEvaluateEvaluatesArithmeticExpressions(string expression, double expected)
    {
        Assert.True(MathExpressionParser.TryEvaluate(expression, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1+")]
    [InlineData("(2 + 3")]
    [InlineData("2 ** 3")]
    [InlineData("1 / 0")]
    [InlineData("NaN")]
    [InlineData("1,5")]
    [InlineData("2 + text")]
    public void TryEvaluateRejectsInvalidOrNonFiniteExpressions(string expression)
    {
        Assert.False(MathExpressionParser.TryEvaluate(expression, out _));
    }
}
