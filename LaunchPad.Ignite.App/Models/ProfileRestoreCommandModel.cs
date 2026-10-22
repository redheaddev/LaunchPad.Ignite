using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ProfileRestoreCommandModel : IAsyncCommandExecutor
{
    private readonly ProfileBackupService _backupService;
    private readonly Func<string, Task<bool>> _confirmRestore;

    public ProfileRestoreCommandModel(ProfileBackupService backupService, Func<string, Task<bool>> confirmRestore)
    {
        _backupService = backupService;
        _confirmRestore = confirmRestore;
    }

    public string Kind => "profileRestore";

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        if (!command.Arguments.TryGetValue("file", out var fileName)
            || _backupService.GetBackupFiles().Count(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            return new CommandExecutionResult(false, $"Backup '{fileName}' was not found.");
        }

        if (!await _confirmRestore(fileName))
        {
            return new CommandExecutionResult(false, "Profile restore was canceled.");
        }

        try
        {
            var backupPath = Path.Combine(_backupService.BackupDirectory, fileName);
            var safetyBackup = _backupService.RestoreBackup(backupPath);
            return new CommandExecutionResult(true, $"Restored profile from '{fileName}'. Existing files were backed up to '{Path.GetFileName(safetyBackup)}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CommandExecutionResult(false, $"Unable to restore profile backup: {exception.Message}");
        }
    }
}
