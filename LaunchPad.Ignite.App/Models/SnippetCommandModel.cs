using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class SnippetCommandModel : ICommandModel, IAsyncCommandExecutor
{
    private readonly SnippetStore _snippetStore;
    private readonly ISecretStore _secretStore;
    private readonly ISnippetDialogService _dialogService;
    private readonly ISecretAccessGate _secretAccessGate;
    private readonly ISecretEntryDialogService _secretEntryDialogService;
    private readonly ISecretTerminalLauncher _terminalLauncher;

    public SnippetCommandModel(
        SnippetStore snippetStore,
        ISecretStore secretStore,
        ISnippetDialogService dialogService,
        ISecretAccessGate secretAccessGate,
        ISecretEntryDialogService secretEntryDialogService,
        ISecretTerminalLauncher terminalLauncher)
    {
        _snippetStore = snippetStore;
        _secretStore = secretStore;
        _dialogService = dialogService;
        _secretAccessGate = secretAccessGate;
        _secretEntryDialogService = secretEntryDialogService;
        _terminalLauncher = terminalLauncher;
    }

    public string Kind => "snippet";

    public CommandExecutionResult Execute(ParsedCommand command)
        => new(false, "Snippet commands require asynchronous execution.");

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        var name = command.Arguments.TryGetValue("name", out var suppliedName) ? suppliedName : string.Empty;
        var newName = command.Arguments.TryGetValue("newName", out var suppliedNewName) ? suppliedNewName : string.Empty;
        try
        {
            return command.Action.Name.ToLowerInvariant() switch
            {
                "set" => await EditAsync(name),
                "edit" => await EditAsync(name, requireExisting: true),
                "remove" => await RemoveAsync(name),
                "rename" => Rename(name, newName),
                "run" => await RunAsync(name, command.Arguments),
                _ => new CommandExecutionResult(false, $"Unknown snippet action '{command.Action.Name}'.")
            };
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to complete snippet '{name}': {exception.Message}");
        }
    }

    private async Task<CommandExecutionResult> RunAsync(
        string name,
        IReadOnlyDictionary<string, string> suppliedArguments)
    {
        var snippet = _snippetStore.Find(name);
        if (snippet is null)
        {
            return new CommandExecutionResult(false, $"Snippet '{name}' was not found.");
        }

        var declaredArguments = snippet.Metadata.Arguments;
        var argumentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (argumentName, value) in suppliedArguments)
        {
            if (string.Equals(argumentName, "name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!declaredArguments.Contains(argumentName, StringComparer.OrdinalIgnoreCase))
            {
                return new CommandExecutionResult(false, $"Snippet '{name}' does not declare an argument named '{argumentName}'.");
            }

            argumentValues.Add(argumentName, value);
        }

        var missingArguments = declaredArguments
            .Where(argument => !argumentValues.ContainsKey(argument))
            .ToArray();
        if (missingArguments.Length > 0)
        {
            var promptedValues = await _dialogService.ShowArgumentsAsync(name, missingArguments);
            if (promptedValues is null)
            {
                return new CommandExecutionResult(false, "Snippet execution canceled.");
            }

            foreach (var (argumentName, value) in promptedValues)
            {
                argumentValues[argumentName] = value;
            }
        }

        if (snippet.Metadata.AllowedSecrets.Count > 0 && !_secretAccessGate.IsUnlocked)
        {
            if (!_secretAccessGate.HasMasterPassword)
            {
                return new CommandExecutionResult(false, "Secret access is unavailable.");
            }

            var password = await _secretEntryDialogService.ShowAsync("Unlock secrets", requireConfirmation: false);
            if (password is null)
            {
                return new CommandExecutionResult(false, "Snippet execution canceled while unlocking secrets.");
            }

            if (!_secretAccessGate.TryUnlock(password))
            {
                return new CommandExecutionResult(false, "The master password is incorrect.");
            }
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in declaredArguments)
        {
            if (!argumentValues.TryGetValue(argument, out var value))
            {
                return new CommandExecutionResult(false, $"A value for snippet argument '{argument}' is required.");
            }

            environment.Add(argument, value);
        }

        foreach (var secretName in snippet.Metadata.AllowedSecrets)
        {
            if (!_secretStore.TryGet(secretName, out var secretValue))
            {
                return new CommandExecutionResult(false, $"Allowed secret '{secretName}' is no longer stored.");
            }

            environment.Add(secretName, secretValue);
        }

        try
        {
            _terminalLauncher.RunScript(_snippetStore.GetScriptPath(snippet), snippet.Metadata.Type, environment);
            return new CommandExecutionResult(true, $"Started snippet '{name}' in a terminal.");
        }
        finally
        {
            environment.Clear();
        }
    }

    private async Task<CommandExecutionResult> EditAsync(string name, bool requireExisting = false)
    {
        var snippet = _snippetStore.Find(name);
        if (requireExisting && snippet is null)
        {
            return new CommandExecutionResult(false, $"Snippet '{name}' was not found.");
        }

        var result = await _dialogService.ShowEditorAsync(name, snippet, _secretStore.GetNames());
        if (result is null)
        {
            return new CommandExecutionResult(false, "Snippet editing canceled.");
        }

        _snippetStore.Save(name, result.Type, result.Arguments, result.AllowedSecrets, result.Script);
        return new CommandExecutionResult(true, $"Saved snippet '{name}'.");
    }

    private async Task<CommandExecutionResult> RemoveAsync(string name)
    {
        if (_snippetStore.Find(name) is null)
        {
            return new CommandExecutionResult(false, $"Snippet '{name}' was not found.");
        }

        if (!await _dialogService.ConfirmRemoveAsync(name))
        {
            return new CommandExecutionResult(false, $"Removal of snippet '{name}' was canceled.");
        }

        return _snippetStore.Remove(name)
            ? new CommandExecutionResult(true, $"Removed snippet '{name}'.")
            : new CommandExecutionResult(false, $"Snippet '{name}' was not found.");
    }

    private CommandExecutionResult Rename(string name, string newName)
        => _snippetStore.Rename(name, newName)
            ? new CommandExecutionResult(true, $"Renamed snippet '{name}' to '{newName}'.")
            : new CommandExecutionResult(false, $"Snippet '{name}' was not found.");
}
