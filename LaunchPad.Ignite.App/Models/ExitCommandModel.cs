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
        "exit",
        "Close the application",
        [new CommandActionDefinition("default", "Close the application", "exit", "exit", [])]);

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.Kind, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command kind '{command.Definition.Kind}' is not supported.");
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
