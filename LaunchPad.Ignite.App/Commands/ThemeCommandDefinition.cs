using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class ThemeCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "theme",
        "theme",
        "Select an available color theme",
        [new CommandActionDefinition(
            "default",
            "Apply a saved color theme",
            "theme <list:themes>",
            "theme",
            [new CommandArgumentDefinition("name", Ref: "list:themes", ListMode: ListSelectionMode.Single)])]);
}