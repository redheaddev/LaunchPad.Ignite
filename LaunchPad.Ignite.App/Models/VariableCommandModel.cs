using System;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class VariableCommandModel : ICommandModel
{
    private readonly CommandSchemaLoader _schemaLoader;
    private readonly Action<CommandSchema> _variablesUpdated;

    public static CommandDefinition Definition { get; } = new(
        "variable",
        "variable",
        "Add or remove a command variable",
        [
            new CommandActionDefinition(
                "add",
                "Add a variable",
                "variable add <name> <value>",
                "variable add",
                [new CommandArgumentDefinition("name"), new CommandArgumentDefinition("value")]),
            new CommandActionDefinition(
                "remove",
                "Remove a variable",
                "variable remove <name>",
                "variable remove",
                [new CommandArgumentDefinition("name")])
        ]);

    public VariableCommandModel(CommandSchemaLoader schemaLoader, Action<CommandSchema> variablesUpdated)
    {
        _schemaLoader = schemaLoader;
        _variablesUpdated = variablesUpdated;
    }

    public string Kind => "variable";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        var name = command.Arguments["name"];
        try
        {
            if (string.Equals(command.Action.Name, "add", StringComparison.OrdinalIgnoreCase))
            {
                var schema = _schemaLoader.AddVariable(name, command.Arguments["value"]);
                _variablesUpdated(schema);
                return new CommandExecutionResult(true, $"Added variable '{name}'.");
            }

            if (string.Equals(command.Action.Name, "remove", StringComparison.OrdinalIgnoreCase))
            {
                if (!_schemaLoader.RemoveVariable(name, out var schema))
                {
                    return new CommandExecutionResult(false, $"Variable '{name}' was not found in user variables.");
                }

                _variablesUpdated(schema);
                return new CommandExecutionResult(true, $"Removed variable '{name}'.");
            }

            return new CommandExecutionResult(false, $"Unknown variable action '{command.Action.Name}'.");
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to update variable '{name}': {exception.Message}");
        }
    }
}
