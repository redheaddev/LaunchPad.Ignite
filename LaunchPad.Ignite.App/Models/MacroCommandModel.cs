using System;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Macros;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class MacroCommandModel : ICommandModel, IAsyncCommandExecutor
{
    private readonly MacroStore _macroStore;
    private readonly IMacroDialogService _dialogService;
    private readonly Action _runtimeChanged;

    public MacroCommandModel(MacroStore macroStore, IMacroDialogService dialogService, Action runtimeChanged)
    {
        _macroStore = macroStore;
        _dialogService = dialogService;
        _runtimeChanged = runtimeChanged;
    }

    public string Kind => "macro";

    public CommandExecutionResult Execute(ParsedCommand command)
        => new(false, "Macro management requires asynchronous execution.");

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        var name = command.Arguments.TryGetValue("name", out var suppliedName) ? suppliedName : string.Empty;
        try
        {
            return command.Action.Name.ToLowerInvariant() switch
            {
                "set" => await EditAsync(name),
                "edit" => await EditAsync(name, requireExisting: true),
                "remove" => await RemoveAsync(name),
                "list" => List(),
                _ => new CommandExecutionResult(false, $"Unknown macro action '{command.Action.Name}'.")
            };
        }
        catch (Exception exception)
        {
            return new CommandExecutionResult(false, $"Unable to complete macro '{name}': {exception.Message}");
        }
    }

    private CommandExecutionResult List()
    {
        var names = _macroStore.GetAll().Select(macro => macro.Metadata.QualifiedName).ToArray();
        return new CommandExecutionResult(true, names.Length == 0
            ? "No macros defined."
            : string.Join(Environment.NewLine, names)) { IsValueOutput = true };
    }

    private async Task<CommandExecutionResult> EditAsync(string qualifiedName, bool requireExisting = false)
    {
        var (name, macroNamespace) = ParseQualifiedName(qualifiedName);
        MacroStore.ValidateMacroName(name);
        var macro = _macroStore.Find(qualifiedName);
        if (requireExisting && macro is null)
        {
            return new CommandExecutionResult(false, $"Macro '{qualifiedName}' was not found.");
        }

        var result = await _dialogService.ShowEditorAsync(qualifiedName, macro);
        if (result is null)
        {
            return new CommandExecutionResult(false, "Macro editing canceled.");
        }

        var metadata = new MacroMetadata(
            macro?.Metadata.Id ?? string.Empty,
            name,
            macroNamespace,
            result.Arguments,
            result.OnError);
        _macroStore.Save(new MacroDefinition(metadata, result.Script));
        _runtimeChanged();
        return new CommandExecutionResult(true, $"Saved macro '{qualifiedName}'.");
    }

    private async Task<CommandExecutionResult> RemoveAsync(string qualifiedName)
    {
        if (_macroStore.Find(qualifiedName) is null)
        {
            return new CommandExecutionResult(false, $"Macro '{qualifiedName}' was not found.");
        }

        if (!await _dialogService.ConfirmRemoveAsync(qualifiedName))
        {
            return new CommandExecutionResult(false, "Macro removal canceled.");
        }

        _macroStore.Remove(qualifiedName);
        _runtimeChanged();
        return new CommandExecutionResult(true, $"Removed macro '{qualifiedName}'.");
    }

    private static (string Name, string? Namespace) ParseQualifiedName(string qualifiedName)
    {
        var separator = qualifiedName.IndexOf(':');
        return separator < 0
            ? (qualifiedName, null)
            : (qualifiedName[(separator + 1)..], qualifiedName[..separator]);
    }
}
