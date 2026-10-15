using System;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class ExitCommandModel : ICommandModel
{
    private readonly Action _shutdown;

    public ExitCommandModel(Action shutdown)
    {
        _shutdown = shutdown;
    }

    public string Kind => "exit";

    public static CommandDefinition Definition { get; } = new(
        "exit",
        "system",
        "Close the application",
        [new CommandActionDefinition("default", "Close the application", "exit", "exit", [])]) { Handler = "exit" };

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        try
        {
            _shutdown();
            return new CommandExecutionResult(true, "Exiting application.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to exit application: {exception.Message}");
        }
    }
}
