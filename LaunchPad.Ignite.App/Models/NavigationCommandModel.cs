using System;
using System.Collections.Generic;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public enum LauncherNavigationTarget
{
    Settings,
    ImportExport,
    Help
}

public sealed class NavigationCommandModel : ICommandModel
{
    private readonly Action<LauncherNavigationTarget> _requestNavigation;

    public NavigationCommandModel(Action<LauncherNavigationTarget> requestNavigation)
    {
        _requestNavigation = requestNavigation;
    }

    public string Kind => "navigation";

    public static IReadOnlyList<CommandDefinition> Definitions { get; } =
    [
        CreateDefinition("settings", "Open application settings"),
        CreateDefinition("import-export", "Open profile import and export"),
        CreateDefinition("help", "Open launcher help")
    ];

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase)
            || !TryParseTarget(command.Definition.Name, out var target))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        _requestNavigation(target);
        return new CommandExecutionResult(true, $"Opening {command.Definition.Name}.");
    }

    private static CommandDefinition CreateDefinition(
        string name,
        string description)
    {
        var usage = name;
        return new CommandDefinition(
            name,
            "system",
            description,
            [new CommandActionDefinition("default", description, usage, usage, [])])
        {
            Handler = "navigation"
        };
    }

    private static bool TryParseTarget(string commandName, out LauncherNavigationTarget target)
    {
        target = commandName.ToLowerInvariant() switch
        {
            "settings" => LauncherNavigationTarget.Settings,
            "import-export" => LauncherNavigationTarget.ImportExport,
            "help" => LauncherNavigationTarget.Help,
            _ => (LauncherNavigationTarget)(-1)
        };
        return Enum.IsDefined(target);
    }
}
