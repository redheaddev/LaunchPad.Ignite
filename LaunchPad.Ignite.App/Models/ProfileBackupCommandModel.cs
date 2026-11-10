using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Secrets;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ProfileBackupCommandModel : IAsyncCommandExecutor
{
    private readonly ProfileBackupService _backupService;
    private readonly ISecretStore _secretStore;
    private readonly ISecretAccessGate _secretAccessGate;
    private readonly ISecretEntryDialogService _secretEntryDialogService;

    public ProfileBackupCommandModel(
        ProfileBackupService backupService,
        ISecretStore secretStore,
        ISecretAccessGate secretAccessGate,
        ISecretEntryDialogService secretEntryDialogService)
    {
        _backupService = backupService;
        _secretStore = secretStore;
        _secretAccessGate = secretAccessGate;
        _secretEntryDialogService = secretEntryDialogService;
    }

    public string Kind => "profileBackup";

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        try
        {
            command.Arguments.TryGetValue("name", out var name);
            if (!string.Equals(command.Action.Name, "secrets", StringComparison.OrdinalIgnoreCase))
            {
                var profileOnlyBackupPath = _backupService.CreateBackup(name);
                return new CommandExecutionResult(true, $"Created profile backup '{Path.GetFileName(profileOnlyBackupPath)}'.");
            }

            if (!_secretAccessGate.HasMasterPassword)
            {
                return new CommandExecutionResult(false, "Set a master password before creating a backup that includes secrets.");
            }

            if (!_secretStore.IsAvailable)
            {
                return new CommandExecutionResult(false, _secretStore.UnavailableReason ?? "Secret storage is unavailable.");
            }

            var password = await _secretEntryDialogService.ShowAsync("Enter master password to encrypt backup secrets", requireConfirmation: false);
            if (password is null)
            {
                return new CommandExecutionResult(false, "Profile backup canceled.");
            }

            if (!_secretAccessGate.TryVerifyPassword(password))
            {
                return new CommandExecutionResult(false, "The master password is incorrect.");
            }

            if (password.Length < 12)
            {
                return new CommandExecutionResult(false, "Use a master password with at least 12 characters to include encrypted secrets.");
            }

            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var secretName in _secretStore.GetNames())
            {
                if (!_secretStore.TryGet(secretName, out var value))
                {
                    return new CommandExecutionResult(false, $"Could not read secret '{secretName}' from secure storage.");
                }

                secrets.Add(secretName, value);
            }

            var backupPath = _backupService.CreateBackup(name, secrets, password);
            return new CommandExecutionResult(true, $"Created profile backup '{Path.GetFileName(backupPath)}' with {secrets.Count} encrypted secret(s).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new CommandExecutionResult(false, $"Unable to create profile backup: {exception.Message}");
        }
    }
}