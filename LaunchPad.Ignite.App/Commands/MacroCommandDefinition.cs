using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class MacroCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "macro",
        "macro",
        "Manage stored command macros",
        [
            new CommandActionDefinition(
                "set",
                "Create or update a macro",
                "macro set <name-or-namespace:name>",
                "macro",
                [new CommandArgumentDefinition("name")]),
            new CommandActionDefinition(
                "edit",
                "Edit a stored macro",
                "macro edit <name-or-namespace:name>",
                "macro",
                [new CommandArgumentDefinition("name", Ref: "list:macros", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "remove",
                "Remove a stored macro",
                "macro remove <name-or-namespace:name>",
                "macro",
                [new CommandArgumentDefinition("name", Ref: "list:macros", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "list",
                "List stored macros",
                "macro list",
                "macro",
                [])
        ]) { Handler = "macro" };
}
