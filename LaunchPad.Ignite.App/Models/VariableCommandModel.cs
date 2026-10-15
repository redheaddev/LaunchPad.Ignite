using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class VariableCommandModel : ICommandModel
{
    private readonly CommandSchemaLoader _schemaLoader;
    private readonly Action<CommandSchema> _variablesUpdated;
    private readonly Func<string, Task<bool>>? _confirmOverwrite;

    public static CommandDefinition Definition { get; } = new(
        "variable",
        "variable",
        "Set or remove a command variable",
        [
            new CommandActionDefinition(
                "set",
                "Set a variable value",
                "variable set <name> <value>",
                "variable set",
                [new CommandArgumentDefinition("name"), new CommandArgumentDefinition("value")]),
            new CommandActionDefinition(
                "remove",
                "Remove a variable",
                "variable remove <name>",
                "variable remove",
                [new CommandArgumentDefinition("name")])
        ]) { Handler = "variable" };

    public VariableCommandModel(
        CommandSchemaLoader schemaLoader,
        Action<CommandSchema> variablesUpdated,
        Func<string, Task<bool>>? confirmOverwrite = null)
    {
        _schemaLoader = schemaLoader;
        _variablesUpdated = variablesUpdated;
        _confirmOverwrite = confirmOverwrite;
    }

    public string Kind => "variable";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        var name = command.Arguments["name"];
        try
        {
            if (IsSetAction(command) && _schemaLoader.ContainsVariable(name))
            {
                return new CommandExecutionResult(false, $"Overwriting variable '{name}' requires confirmation.");
            }

            return ExecuteCore(command);
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to update variable '{name}': {exception.Message}");
        }
    }

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        var name = command.Arguments["name"];
        try
        {
            if (IsSetAction(command)
                && _schemaLoader.ContainsVariable(name)
                && (_confirmOverwrite is null || !await _confirmOverwrite(name)))
            {
                return new CommandExecutionResult(false, $"Overwrite of variable '{name}' was cancelled.");
            }

            return ExecuteCore(command);
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to update variable '{name}': {exception.Message}");
        }
    }

    private CommandExecutionResult ExecuteCore(ParsedCommand command)
    {
        var name = command.Arguments["name"];
        if (IsSetAction(command))
        {
            var schema = _schemaLoader.SetVariable(name, command.Arguments["value"]);
            _variablesUpdated(schema);
            return new CommandExecutionResult(true, $"Set variable '{name}'.");
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

    private static bool IsSetAction(ParsedCommand command)
        => string.Equals(command.Action.Name, "set", StringComparison.OrdinalIgnoreCase);
}
