using System;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class HistorySuggestionCommandModel(
    HistorySuggestionPreferenceStore preferenceStore,
    Action? preferenceChanged = null) : ICommandModel
{
    public string Kind => "historySuggestions";

    public static CommandDefinition Definition { get; } = new(
        "history-suggestions",
        "system",
        "Control history suggestions in completion picklists",
        [
            new CommandActionDefinition(
                "toggle",
                "Toggle history suggestions in completion picklists",
                "history-suggestions toggle",
                "history-suggestions toggle",
                [])
            {
                KeyboardShortcut = "ctrl+shift+h",
                ExecuteOnKeyboardShortcut = true
            },
            new CommandActionDefinition("on", "Enable history suggestions in completion picklists", "history-suggestions on", "history-suggestions on", []),
            new CommandActionDefinition("off", "Disable history suggestions in completion picklists", "history-suggestions off", "history-suggestions off", [])
        ]) { Handler = "historySuggestions" };

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        var enabled = command.Action.Name.ToLowerInvariant() switch
        {
            "toggle" => preferenceStore.Toggle(),
            "on" => SetEnabled(true),
            "off" => SetEnabled(false),
            _ => (bool?)null
        };
        if (enabled.HasValue)
        {
            preferenceChanged?.Invoke();
        }

        return enabled.HasValue
            ? new CommandExecutionResult(true, $"History suggestions {(enabled.Value ? "enabled" : "disabled")}.")
            : new CommandExecutionResult(false, $"Unknown history suggestions action '{command.Action.Name}'.");
    }

    private bool SetEnabled(bool enabled)
    {
        preferenceStore.SetEnabled(enabled);
        return enabled;
    }
}
