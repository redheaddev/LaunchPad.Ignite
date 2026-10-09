using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class ExtensionCommandModel : ICommandModel
{
    private readonly ExtensionStateStore _stateStore;
    private readonly HashSet<string> _extensionIds;
    private readonly Action? _stateChanged;
    private readonly ExtensionPackageManager _packageManager;

    public ExtensionCommandModel(
        ExtensionStateStore stateStore,
        IEnumerable<string> extensionIds,
        Action? stateChanged = null,
        ExtensionPackageManager? packageManager = null)
    {
        _stateStore = stateStore;
        _extensionIds = extensionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _stateChanged = stateChanged;
        _packageManager = packageManager ?? new ExtensionPackageManager();
    }

    public string Kind => "extension";

    public static CommandDefinition Definition { get; } = new(
            "extension",
            "extension",
            "Manage installed extensions",
            [
                new CommandActionDefinition(
                    "install",
                    "Install or replace an extension from a ZIP package",
                    "extension install <zip-path>",
                    "extension install",
                    [new CommandArgumentDefinition("zip-path")]),
                new CommandActionDefinition(
                    "uninstall",
                    "Uninstall a user-installed extension immediately",
                    "extension uninstall <id>",
                    "extension uninstall",
                    [new CommandArgumentDefinition(
                        "id",
                        Ref: ExtensionIdsArgumentValueProvider.Reference,
                        ListMode: ListSelectionMode.Multiple)]),
                new CommandActionDefinition(
                    "enable",
                    "Enable an extension immediately",
                    "extension enable <id>",
                    "extension enable",
                    [new CommandArgumentDefinition(
                        "id",
                        Ref: ExtensionIdsArgumentValueProvider.Reference,
                        ListMode: ListSelectionMode.Multiple)]),
                new CommandActionDefinition(
                    "disable",
                    "Disable an extension immediately",
                    "extension disable <id>",
                    "extension disable",
                    [new CommandArgumentDefinition(
                        "id",
                        Ref: ExtensionIdsArgumentValueProvider.Reference,
                        ListMode: ListSelectionMode.Multiple)])
            ]);

    public static string FormatList(IEnumerable<LoadedExtensionInfo> extensions, ExtensionStateStore stateStore)
    {
        var entries = extensions.OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (entries.Length == 0)
        {
            return "No extensions installed.";
        }

        return string.Join(
            Environment.NewLine,
            entries.Select(extension =>
                $"{extension.Id} | {extension.Version} [{(stateStore.IsEnabled(extension.Id) ? "enabled" : "disabled")}]"));
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (string.Equals(command.Action.Name, "install", StringComparison.OrdinalIgnoreCase))
        {
            return Install(command.Arguments["zip-path"]);
        }

        if (string.Equals(command.Action.Name, "uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return Uninstall(command.Arguments["id"]);
        }

        if (!string.Equals(command.Action.Name, "enable", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.Action.Name, "disable", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Unknown extension action '{command.Action.Name}'.");
        }

        var ids = command.Arguments["id"].Split(',').Select(id => id.Trim()).ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
        {
            return new CommandExecutionResult(false, "At least one extension ID is required.");
        }

        var enabled = string.Equals(command.Action.Name, "enable", StringComparison.OrdinalIgnoreCase);
        var state = enabled ? "enabled" : "disabled";
        var extensionNames = FormatExtensionNames(ids);
        var unknownId = ids.FirstOrDefault(id => !_extensionIds.Contains(id));
        if (unknownId is not null)
        {
            return new CommandExecutionResult(false, $"Extension '{unknownId}' was not found.");
        }

        var disabledIds = _stateStore.GetDisabledExtensionIds();
        var previousStates = ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, id => !disabledIds.Contains(id), StringComparer.OrdinalIgnoreCase);
        if (previousStates.Values.All(previousState => previousState == enabled))
        {
            var verb = ids.Length == 1 ? "is" : "are";
            return new CommandExecutionResult(true, $"{extensionNames} {verb} already {state}.");
        }

        try
        {
            _stateStore.SetEnabled(ids, enabled);
            _stateChanged?.Invoke();
            return new CommandExecutionResult(true, $"{extensionNames} {state}.");
        }
        catch (Exception exception)
        {
            try
            {
                _stateStore.SetEnabled(previousStates);
            }
            catch (Exception rollbackException)
            {
                return new CommandExecutionResult(
                    false,
                    $"Unable to update {extensionNames}: {exception.Message} State rollback also failed: {rollbackException.Message}");
            }

            return new CommandExecutionResult(false, $"Unable to update {extensionNames}: {exception.Message}");
        }
    }

    private CommandExecutionResult Install(string packagePath)
    {
        string? extensionId = null;
        var previousEnabledState = true;
        try
        {
            extensionId = _packageManager.Install(packagePath, installedId =>
            {
                extensionId = installedId;
                previousEnabledState = _stateStore.IsEnabled(installedId);
                _stateStore.SetEnabled(installedId, enabled: true);
                _stateChanged?.Invoke();
            });
            return new CommandExecutionResult(true, $"Extension '{extensionId}' installed.");
        }
        catch (Exception exception)
        {
            if (extensionId is not null)
            {
                try
                {
                    _stateStore.SetEnabled(extensionId, previousEnabledState);
                }
                catch (Exception rollbackException)
                {
                    return new CommandExecutionResult(false,
                        $"Unable to install extension: {exception.Message} State rollback also failed: {rollbackException.Message}");
                }
            }

            return new CommandExecutionResult(false, $"Unable to install extension: {exception.Message}");
        }
    }

    private CommandExecutionResult Uninstall(string argument)
    {
        var ids = argument.Split(',').Select(id => id.Trim()).ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
        {
            return new CommandExecutionResult(false, "At least one extension ID is required.");
        }

        ids = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var disabledIds = _stateStore.GetDisabledExtensionIds();
        var previousStates = ids.ToDictionary(
            id => id,
            id => !disabledIds.Contains(id),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            _packageManager.Uninstall(ids, uninstalledIds =>
            {
                foreach (var uninstalledId in uninstalledIds)
                {
                    _stateStore.Remove(uninstalledId);
                }

                _stateChanged?.Invoke();
            });
            return new CommandExecutionResult(true, $"{FormatExtensionNames(ids)} uninstalled.");
        }
        catch (Exception exception)
        {
            try
            {
                _stateStore.SetEnabled(previousStates);
            }
            catch (Exception rollbackException)
            {
                return new CommandExecutionResult(false,
                    $"Unable to uninstall {FormatExtensionNames(ids)}: {exception.Message} State rollback also failed: {rollbackException.Message}");
            }

            return new CommandExecutionResult(false,
                $"Unable to uninstall {FormatExtensionNames(ids)}: {exception.Message}");
        }
    }

    private static string FormatExtensionNames(IReadOnlyList<string> ids)
        => ids.Count == 1
            ? $"Extension '{ids[0]}'"
            : $"Extensions '{string.Join("', '", ids)}'";
}
