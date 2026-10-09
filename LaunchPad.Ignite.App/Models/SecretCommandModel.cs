using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class SecretCommandModel
{
    private static readonly TimeSpan ClipboardExpiry = TimeSpan.FromSeconds(15);
    private readonly ISecretStore _secretStore;
    private readonly ISecretAccessGate _accessGate;
    private readonly ISecretEntryDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly ISecretTerminalLauncher _terminalLauncher;

    public SecretCommandModel(
        ISecretStore secretStore,
        ISecretAccessGate accessGate,
        ISecretEntryDialogService dialogService,
        IClipboardService clipboardService,
        ISecretTerminalLauncher terminalLauncher)
    {
        _secretStore = secretStore;
        _accessGate = accessGate;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _terminalLauncher = terminalLauncher;
    }

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        try
        {
            return command.Action.Name.ToLowerInvariant() switch
            {
                "set" => await SetSecretAsync(command.Arguments["secretName"]),
                "show" => await ShowSecretAsync(command.Arguments["secretName"]),
                "master" => await SetMasterPasswordAsync(command.Arguments["operation"]),
                "unlock" => await UnlockAsync(),
                "lock" => Lock(),
                "remove" => Remove(command.Arguments["secretName"]),
                "rename" => Rename(command.Arguments["secretName"], command.Arguments["newName"]),
                "copy-value" => await CopyValueAsync(command.Arguments["secretName"], environmentAssignment: false),
                "copy-env" => await CopyValueAsync(command.Arguments["secretName"], environmentAssignment: true),
                "shell" => await OpenShellAsync(command.Arguments["secretNames"], command.Arguments.GetValueOrDefault("path")),
                _ => new CommandExecutionResult(false, $"Unknown secret action '{command.Action.Name}'.")
            };
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Secret action failed: {exception.GetType().Name}.");
            return new CommandExecutionResult(false, "Unable to complete the secret action.");
        }
    }

    private async Task<CommandExecutionResult> SetSecretAsync(string name)
    {
        if (!IsValidName(name))
        {
            return InvalidName();
        }

        var value = await _dialogService.ShowAsync($"Set secret '{name}'", requireConfirmation: true);
        if (value is null)
        {
            return new CommandExecutionResult(false, "Secret entry canceled.");
        }

        if (value.Length == 0)
        {
            return new CommandExecutionResult(false, "Secret values cannot be empty.");
        }

        _secretStore.Set(name, value);
        return new CommandExecutionResult(true, $"Secret '{name}' saved.");
    }

    private async Task<CommandExecutionResult> ShowSecretAsync(string name)
    {
        if (!IsValidName(name))
        {
            return InvalidName();
        }

        var authorizationResult = await EnsureAuthorizedAsync();
        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        if (!_secretStore.TryGet(name, out var value))
        {
            return new CommandExecutionResult(false, $"Secret '{name}' was not found.");
        }

        await _dialogService.ShowSecretAsync($"Secret '{name}'", value);
        return new CommandExecutionResult(true, "Secret displayed.");
    }

    private async Task<CommandExecutionResult> SetMasterPasswordAsync(string operation)
    {
        if (!string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, "Usage: secret master set");
        }

        var password = await _dialogService.ShowAsync("Set master password", requireConfirmation: false);
        if (password is null)
        {
            return new CommandExecutionResult(false, "Master-password setup canceled.");
        }

        if (password.Length == 0)
        {
            return new CommandExecutionResult(false, "The master password cannot be empty.");
        }

        if (!_accessGate.TrySetMasterPassword(password))
        {
            return new CommandExecutionResult(false, "Unable to set the master password.");
        }

        return new CommandExecutionResult(true, "Master password set. Secret access is unlocked for 60 seconds.");
    }

    private async Task<CommandExecutionResult> UnlockAsync()
    {
        if (!_accessGate.HasMasterPassword)
        {
            return new CommandExecutionResult(false, "No master password is set. Use 'secret master set' first.");
        }

        if (_accessGate.IsUnlocked)
        {
            return new CommandExecutionResult(true, "Secret access is already unlocked.");
        }

        var password = await _dialogService.ShowAsync("Unlock secrets", requireConfirmation: false);
        if (password is null)
        {
            return new CommandExecutionResult(false, "Unlock canceled.");
        }

        return _accessGate.TryUnlock(password)
            ? new CommandExecutionResult(true, "Secret access unlocked for 60 seconds.")
            : new CommandExecutionResult(false, "The master password is incorrect.");
    }

    private CommandExecutionResult Lock()
    {
        if (!_accessGate.HasMasterPassword)
        {
            return new CommandExecutionResult(false, "Set a master password before locking secret access.");
        }

        _accessGate.Lock();
        return new CommandExecutionResult(true, "Secret access locked.");
    }

    private CommandExecutionResult Remove(string name)
    {
        if (!IsValidName(name))
        {
            return InvalidName();
        }

        return _secretStore.Remove(name)
            ? new CommandExecutionResult(true, $"Secret '{name}' removed.")
            : new CommandExecutionResult(false, $"Secret '{name}' was not found.");
    }

    private CommandExecutionResult Rename(string name, string newName)
    {
        if (!IsValidName(name) || !IsValidName(newName))
        {
            return InvalidName();
        }

        if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, "The new secret name must be different.");
        }

        if (!_secretStore.TryGet(name, out var value))
        {
            return new CommandExecutionResult(false, $"Secret '{name}' was not found.");
        }

        if (_secretStore.GetNames().Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Secret '{newName}' already exists.");
        }

        _secretStore.Set(newName, value);
        if (!_secretStore.Remove(name))
        {
            _secretStore.Remove(newName);
            return new CommandExecutionResult(false, $"Unable to rename secret '{name}'.");
        }

        return new CommandExecutionResult(true, $"Secret '{name}' renamed to '{newName}'.");
    }

    private async Task<CommandExecutionResult> CopyValueAsync(string name, bool environmentAssignment)
    {
        if (!IsValidName(name))
        {
            return InvalidName();
        }

        var authorizationResult = await EnsureAuthorizedAsync();
        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        if (!_secretStore.TryGet(name, out var value))
        {
            return new CommandExecutionResult(false, $"Secret '{name}' was not found.");
        }

        var clipboardText = environmentAssignment
            ? $"$env:{name}='{value.Replace("'", "''", StringComparison.Ordinal)}'"
            : value;
        await _clipboardService.CopyWithExpiryAsync(clipboardText, ClipboardExpiry);
        return new CommandExecutionResult(true, "Secret copied to the clipboard. It will be cleared in 15 seconds if unchanged.");
    }

    private async Task<CommandExecutionResult> OpenShellAsync(string namesCsv, string? workingDirectory)
    {
        var names = namesCsv.Split(',').Select(name => name.Trim()).ToArray();
        if (names.Length == 0 || names.Any(name => !IsValidName(name)))
        {
            return InvalidName();
        }

        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        {
            return new CommandExecutionResult(false, "Secret names must not be repeated.");
        }

        var authorizationResult = await EnsureAuthorizedAsync();
        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!_secretStore.TryGet(name, out var value))
            {
                return new CommandExecutionResult(false, $"Secret '{name}' was not found.");
            }

            environment.Add(name, value);
        }

        _terminalLauncher.OpenShell(environment, workingDirectory);
        return new CommandExecutionResult(true, "Terminal opened with the selected secrets in its environment.");
    }

    private async Task<CommandExecutionResult?> EnsureAuthorizedAsync()
    {
        if (!_accessGate.HasMasterPassword || _accessGate.IsUnlocked)
        {
            return null;
        }

        var password = await _dialogService.ShowAsync("Unlock secrets", requireConfirmation: false);
        if (password is null)
        {
            return new CommandExecutionResult(false, "Unlock canceled.");
        }

        return _accessGate.TryUnlock(password)
            ? null
            : new CommandExecutionResult(false, "The master password is incorrect.");
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128 || !IsNameStart(name[0]))
        {
            return false;
        }

        return name.Skip(1).All(character => IsNameStart(character) || character is >= '0' and <= '9');
    }

    private static bool IsNameStart(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';

    private static CommandExecutionResult InvalidName()
        => new(false, "Secret names must be valid environment-variable names (letters, digits, and underscores; not starting with a digit).");
}
