using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class EditCommandDefinition
{
    public static CommandDefinition Definition { get; } = new(
        "edit",
        "system",
        "Open the command editor, optionally selecting a command",
        [new CommandActionDefinition(
            "default",
            "Open the Commands editor",
            "edit [<list:commands>]",
            "edit",
            [new CommandArgumentDefinition("name", Optional: true, Ref: "list:commands", ListMode: ListSelectionMode.Single)])]) { Handler = "edit" };
}
