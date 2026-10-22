using System;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.WindowBehavior;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class LauncherModeCommandModel : ICommandModel
{
    private readonly Func<LauncherWindowMode> _getCurrentMode;
    private readonly Action<LauncherWindowMode> _setMode;

    public LauncherModeCommandModel(
        Func<LauncherWindowMode> getCurrentMode,
        Action<LauncherWindowMode> setMode)
    {
        _getCurrentMode = getCurrentMode;
        _setMode = setMode;
    }

    public string Kind => "mode";

    public static CommandDefinition Definition { get; } = new(
        "mode",
        "system",
        "Switch between main and mini window modes",
        [
            new CommandActionDefinition("toggle", "Toggle main/mini mode", "mode toggle", "mode toggle", []),
            new CommandActionDefinition("main", "Show the main window", "mode main", "mode main", []),
            new CommandActionDefinition("mini", "Show the mini window", "mode mini", "mode mini", [])
        ]) { Handler = "mode" };

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        if (!LauncherModeCommandParser.TryParse(command.Action.Name, _getCurrentMode(), out var targetMode))
        {
            return new CommandExecutionResult(false, $"Unknown mode action '{command.Action.Name}'.");
        }

        _setMode(targetMode);
        return new CommandExecutionResult(true, $"Switched to {targetMode.ToString().ToLowerInvariant()} mode.");
    }
}
