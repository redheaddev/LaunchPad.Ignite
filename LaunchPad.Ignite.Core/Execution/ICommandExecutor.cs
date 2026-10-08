using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.Core.Execution;

public interface ICommandExecutor
{
    string Kind { get; }

    CommandExecutionResult Execute(ParsedCommand command);
}

public sealed record CommandExecutionResult(bool Succeeded, string Message);
