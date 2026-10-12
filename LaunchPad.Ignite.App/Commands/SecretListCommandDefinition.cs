using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class SecretListCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "*",
        "secretList",
        "List saved secret names",
        [new CommandActionDefinition(
            "default",
            "List saved secret names",
            "* [list:secrets]",
            "*",
            [new CommandArgumentDefinition(
                "secretNames",
                Optional: true,
                Ref: "list:secrets",
                InputType: "csv",
                ListMode: ListSelectionMode.Multiple)])]);
}