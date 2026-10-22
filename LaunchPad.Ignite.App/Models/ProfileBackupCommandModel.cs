using System;
using System.IO;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ProfileBackupCommandModel : ICommandModel
{
    private readonly ProfileBackupService _backupService;

    public ProfileBackupCommandModel(ProfileBackupService backupService)
    {
        _backupService = backupService;
    }

    public string Kind => "profileBackup";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        try
        {
            command.Arguments.TryGetValue("name", out var name);
            var backupPath = _backupService.CreateBackup(name);
            return new CommandExecutionResult(true, $"Created profile backup '{Path.GetFileName(backupPath)}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CommandExecutionResult(false, $"Unable to create profile backup: {exception.Message}");
        }
    }
}
