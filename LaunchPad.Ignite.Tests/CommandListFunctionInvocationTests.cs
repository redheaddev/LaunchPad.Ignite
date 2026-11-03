using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class CommandListFunctionInvocationTests
{
    [Theory]
    [InlineData("@ado:repos(Architecture)", "ado:repos", "Architecture")]
    [InlineData("@ado:repos(\"Architecture Team\")", "ado:repos", "Architecture Team")]
    public void TryParseInvocationParsesFunctionNameAndArguments(
        string expression,
        string expectedFunctionName,
        string expectedArgument)
    {
        var parsed = CommandListFunctionCall.TryParseInvocation(expression, out var invocation);

        Assert.True(parsed);
        Assert.Equal(expectedFunctionName, invocation.FunctionName);
        Assert.Equal(expectedArgument, Assert.Single(invocation.Arguments));
    }

    [Theory]
    [InlineData("@ado:repos(", false)]
    [InlineData("@ado:repos(Architecture)", true)]
    [InlineData("prefix @ado:repos(Architecture)", false)]
    [InlineData("@ado:repos(Architecture,)", false)]
    public void TryParseInvocationRequiresACompleteStandaloneCall(string expression, bool expected)
    {
        Assert.Equal(expected, CommandListFunctionCall.TryParseInvocation(expression, out _));
    }
}
