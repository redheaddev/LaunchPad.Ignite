using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class SecretCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "secret",
        "secret",
        "Manage user secrets",
        [
            new CommandActionDefinition(
                "shell",
                "Open a terminal with selected secrets in its environment",
                "secret shell <list:secretName> [path]",
                "secret",
                [
                    new CommandArgumentDefinition(
                        "secretNames",
                        Ref: "list:secrets",
                        InputType: "csv",
                        ListMode: ListSelectionMode.Multiple),
                    new CommandArgumentDefinition("path", Optional: true)
                ]),
            new CommandActionDefinition(
                "copy-env",
                "Copy a secret as an environment assignment",
                "secret copy-env <secretName>",
                "secret",
                [new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "copy-value",
                "Copy a secret value",
                "secret copy-value <secretName>",
                "secret",
                [new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "show",
                "Show a saved secret",
                "secret show <secretName>",
                "secret",
                [new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition("lock", "Lock secret access", "secret lock", "secret", []),
            new CommandActionDefinition("unlock", "Unlock secret access", "secret unlock", "secret", []),
            new CommandActionDefinition(
                "master",
                "Set the master password",
                "secret master set",
                "secret",
                [new CommandArgumentDefinition("operation")]),
            new CommandActionDefinition(
                "remove",
                "Remove a secret",
                "secret remove <secretName>",
                "secret",
                [new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single)]),
            new CommandActionDefinition(
                "rename",
                "Rename a secret",
                "secret rename <secretName> <newName>",
                "secret",
                [
                    new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single),
                    new CommandArgumentDefinition("newName", Ref: "list:secrets", ListMode: ListSelectionMode.Single, AllowCustomValues: true)
                ]),
            new CommandActionDefinition(
                "set",
                "Add or update a secret",
                "secret set <secretName>",
                "secret",
                [new CommandArgumentDefinition("secretName", Ref: "list:secrets", ListMode: ListSelectionMode.Single, AllowCustomValues: true)])
        ]);
}
