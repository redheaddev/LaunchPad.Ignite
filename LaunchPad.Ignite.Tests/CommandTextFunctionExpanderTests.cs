using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandTextFunctionExpanderTests
{
    [Fact]
    public async Task ExpandEvaluatesNestedCallsAndCachesRepeatedCallsOnlyWithinExpansion()
    {
        var echo = new CommandFunctionDefinition("echo", "test", [new("value")], Namespace: "demo");
        var join = new CommandFunctionDefinition("join", "test", [new("first"), new("second")], Namespace: "demo");
        var executor = new RecordingFunctionExecutor((function, arguments) =>
            string.Equals(function.Name, "echo", StringComparison.OrdinalIgnoreCase)
                ? arguments[0]
                : $"{arguments[0]}|{arguments[1]}");
        var expander = new CommandTextFunctionExpander([echo, join], executor);
        const string expression = "@demo:join(@demo:echo(\"hello, world\"), \"second\")";

        var firstExpansion = await expander.ExpandAsync($"{expression} / {expression}");
        var secondExpansion = await expander.ExpandAsync(expression);

        Assert.Equal("hello, world|second / hello, world|second", firstExpansion);
        Assert.Equal("hello, world|second", secondExpansion);
        Assert.Equal(2, executor.Calls.Count(call => call.Function.Name == "echo"));
        Assert.Equal(2, executor.Calls.Count(call => call.Function.Name == "join"));
    }

    [Fact]
    public async Task ExpandLeavesOrdinaryAtTextUntouchedAndRejectsUnknownFunctions()
    {
        var expander = new CommandTextFunctionExpander([], new RecordingFunctionExecutor((_, _) => string.Empty));

        Assert.Equal("mail user@example.test", await expander.ExpandAsync("mail user@example.test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => expander.ExpandAsync("@Missing()"));
    }

    [Fact]
    public async Task ExpandRejectsFunctionArgumentCountMismatch()
    {
        var function = new CommandFunctionDefinition("lookup", "test", [new("id")]);
        var expander = new CommandTextFunctionExpander([function], new RecordingFunctionExecutor((_, _) => "value"));

        await Assert.ThrowsAsync<FormatException>(() => expander.ExpandAsync("@lookup()"));
    }

    [Fact]
    public async Task ExpandAcceptsOmittedOrSuppliedTrailingOptionalArguments()
    {
        var function = new CommandFunctionDefinition(
            "format",
            "test",
            [new("value"), new("suffix", Optional: true)]);
        var executor = new RecordingFunctionExecutor((_, arguments) => string.Join("|", arguments));
        var expander = new CommandTextFunctionExpander([function], executor);

        var result = await expander.ExpandAsync("@format(value) / @format(value, suffix)");

        Assert.Equal("value / value|suffix", result);
        Assert.Equal(1, executor.Calls[0].Arguments.Count);
        Assert.Equal(2, executor.Calls[1].Arguments.Count);
    }

    private sealed class RecordingFunctionExecutor(
        Func<CommandFunctionDefinition, IReadOnlyList<string>, string> execute) : ICommandFunctionExecutor
    {
        public List<(CommandFunctionDefinition Function, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<string> ExecuteAsync(
            CommandFunctionDefinition function,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add((function, arguments));
            return Task.FromResult(execute(function, arguments));
        }
    }
}
