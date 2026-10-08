using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public interface ICommandModel
{
    string Kind { get; }

    CommandExecutionResult Execute(ParsedCommand command);
}

public sealed record CommandExecutionResult(bool Succeeded, string Message);
