using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class ProfileBackupCommandDefinitions
{
    public static CommandDefinition Backup { get; } = new(
        "backup",
        "system",
        "Create a ZIP backup of the LaunchPad profile",
        [new CommandActionDefinition(
            "default",
            "Back up profile files",
            "backup [name]",
            "backup",
            [new CommandArgumentDefinition("name", Optional: true)])]) { Handler = "profileBackup" };

    public static CommandDefinition Restore { get; } = new(
        "restore",
        "system",
        "Restore the profile from a backup file",
        [new CommandActionDefinition(
            "default",
            "Restore profile files from a backup",
            "restore <file>",
            "restore",
            [new CommandArgumentDefinition(
                "file",
                Ref: ProfileBackupFilesArgumentValueProvider.Reference,
                ListMode: ListSelectionMode.Single)])]) { Handler = "profileRestore" };
}
