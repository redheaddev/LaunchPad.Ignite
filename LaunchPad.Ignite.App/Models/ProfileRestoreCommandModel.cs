using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Secrets;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public enum ProfileRestoreChoice
{
    Cancel,
    RestoreWithoutBackup,
    BackupAndRestore
}

public sealed class ProfileRestoreCommandModel : IAsyncCommandExecutor
{
    private readonly ProfileBackupService _backupService;
    private readonly Func<string, Task<ProfileRestoreChoice>> _confirmRestore;
    private readonly ISecretStore _secretStore;
    private readonly ISecretAccessGate _secretAccessGate;
    private readonly ISecretEntryDialogService _secretEntryDialogService;

    public ProfileRestoreCommandModel(
        ProfileBackupService backupService,
        Func<string, Task<ProfileRestoreChoice>> confirmRestore,
        ISecretStore secretStore,
        ISecretAccessGate secretAccessGate,
        ISecretEntryDialogService secretEntryDialogService)
    {
        _backupService = backupService;
        _confirmRestore = confirmRestore;
        _secretStore = secretStore;
        _secretAccessGate = secretAccessGate;
        _secretEntryDialogService = secretEntryDialogService;
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

        var restoreChoice = await _confirmRestore(fileName);
        if (restoreChoice == ProfileRestoreChoice.Cancel)
        {
            return new CommandExecutionResult(false, "Profile restore was canceled.");
        }

        var backupPath = Path.Combine(_backupService.BackupDirectory, fileName);
        var importedNames = new List<string>();
        try
        {
            IReadOnlyDictionary<string, string>? secrets = null;
            if (_backupService.BackupContainsSecrets(backupPath))
            {
                if (!_secretAccessGate.HasMasterPassword)
                {
                    return new CommandExecutionResult(false, "Set a master password before restoring encrypted secrets.");
                }

                if (!_secretStore.IsAvailable)
                {
                    return new CommandExecutionResult(false, _secretStore.UnavailableReason ?? "Secret storage is unavailable.");
                }

                var password = await _secretEntryDialogService.ShowAsync("Enter master password to restore backup secrets", requireConfirmation: false);
                if (password is null)
                {
                    return new CommandExecutionResult(false, "Profile restore canceled while unlocking backup secrets.");
                }

                if (!_secretAccessGate.TryVerifyPassword(password))
                {
                    return new CommandExecutionResult(false, "The master password is incorrect.");
                }

                secrets = _backupService.ReadSecrets(backupPath, password);
            }

            var skippedSecretCount = 0;
            if (secrets is not null)
            {
                (importedNames, skippedSecretCount) = ImportSecretsWithoutReplacingExisting(secrets);
            }

            try
            {
                var safetyBackup = _backupService.RestoreBackup(
                    backupPath,
                    createSafetyBackup: restoreChoice == ProfileRestoreChoice.BackupAndRestore);
                var secretSummary = secrets is null
                    ? string.Empty
                    : $" Imported {importedNames.Count} secret(s); skipped {skippedSecretCount} existing secret(s).";
                var restoreSummary = safetyBackup is null
                    ? $"Restored profile from '{fileName}' without creating a safety backup."
                    : $"Restored profile from '{fileName}'. Existing files were backed up to '{Path.GetFileName(safetyBackup)}'.";
                return new CommandExecutionResult(true, restoreSummary + secretSummary);
            }
            catch
            {
                RemoveImportedSecrets(importedNames);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return new CommandExecutionResult(false, $"Unable to restore profile backup: {exception.Message}");
        }
    }

    private (List<string> ImportedNames, int SkippedCount) ImportSecretsWithoutReplacingExisting(
        IReadOnlyDictionary<string, string> secrets)
    {
        var existingNames = new HashSet<string>(_secretStore.GetNames(), StringComparer.OrdinalIgnoreCase);
        var importedNames = new List<string>();
        var skippedCount = 0;
        try
        {
            foreach (var (name, value) in secrets)
            {
                if (!existingNames.Add(name))
                {
                    skippedCount++;
                    continue;
                }

                _secretStore.Set(name, value);
                importedNames.Add(name);
            }
        }
        catch (Exception exception)
        {
            try
            {
                RemoveImportedSecrets(importedNames);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException("Secret import failed and some newly imported secrets could not be removed.", rollbackException);
            }

            throw new InvalidOperationException("Could not import secrets from the backup.", exception);
        }

        return (importedNames, skippedCount);
    }

    private void RemoveImportedSecrets(IEnumerable<string> names)
    {
        var rollbackFailed = false;
        foreach (var name in names)
        {
            try
            {
                _secretStore.Remove(name);
            }
            catch
            {
                rollbackFailed = true;
            }
        }

        if (rollbackFailed)
        {
            throw new InvalidOperationException("Some newly imported secrets could not be removed.");
        }
    }
}