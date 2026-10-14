using System;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class EditCommandModel : ICommandExecutor
{
    private readonly Action<string?> _openEditor;

    public EditCommandModel(Action<string?> openEditor)
    {
        _openEditor = openEditor;
    }

    public string Kind => "edit";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        command.Arguments.TryGetValue("name", out var name);
        _openEditor(name);
        return new CommandExecutionResult(true, name is null
            ? "Opening the command editor."
            : $"Opening the command editor for '{name}'.");
    }
}
