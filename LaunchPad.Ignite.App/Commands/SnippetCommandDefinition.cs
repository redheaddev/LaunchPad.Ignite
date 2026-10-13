using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class SnippetCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "snippet",
        "snippet",
        "Manage and run stored snippets",
        [
            new CommandActionDefinition(
                "set",
                "Create or update a snippet",
                "snippet set <name>",
                "snippet",
                [new CommandArgumentDefinition("name", Ref: "list:snippets", ListMode: ListSelectionMode.Single, AllowCustomValues: true)]),
            new CommandActionDefinition(
                "edit",
                "Edit a stored snippet",
                "snippet edit <name>",
                "snippet",
                [new CommandArgumentDefinition("name", Ref: "list:snippets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "remove",
                "Remove a stored snippet",
                "snippet remove <name>",
                "snippet",
                [new CommandArgumentDefinition("name", Ref: "list:snippets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "rename",
                "Rename a stored snippet",
                "snippet rename <name> <newName>",
                "snippet",
                [
                    new CommandArgumentDefinition("name", Ref: "list:snippets", ListMode: ListSelectionMode.Single),
                    new CommandArgumentDefinition("newName")
                ]),
            new CommandActionDefinition(
                "run",
                "Run a stored snippet in a terminal",
                "snippet run <name>",
                "snippet",
                [new CommandArgumentDefinition("name", Ref: "list:snippets", ListMode: ListSelectionMode.Single)])
            {
                AllowAdditionalNamedArguments = true
            }
        ]);
}
