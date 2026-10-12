using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public static class ProfileBackupCommandDefinitions
{
    public static CommandDefinition Backup { get; } = new(
        "backup",
        "profileBackup",
        "Create a ZIP backup of the LaunchPad profile",
        [new CommandActionDefinition(
            "default",
            "Back up profile files",
            "backup [name]",
            "backup",
            [new CommandArgumentDefinition("name", Optional: true)])]);

    public static CommandDefinition Restore { get; } = new(
        "restore",
        "profileRestore",
        "Restore the profile from a backup file",
        [new CommandActionDefinition(
            "default",
            "Restore profile files from a backup",
            "restore <file>",
            "restore",
            [new CommandArgumentDefinition(
                "file",
                Ref: ProfileBackupFilesArgumentValueProvider.Reference,
                ListMode: ListSelectionMode.Single)])]);
}
