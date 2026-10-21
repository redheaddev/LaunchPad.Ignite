using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    private readonly HashSet<string> _commandPackIds;
    private readonly HashSet<string> _componentIds;
    private readonly Action? _stateChanged;
    private readonly ExtensionPackageManager _packageManager;
    private readonly CommandPackLoader _commandPackLoader;
    private readonly Func<string, Task<bool>>? _confirmPackReplacement;
    private readonly Func<string, IReadOnlySet<string>>? _existingCommandNames;

    public ExtensionCommandModel(
        ExtensionStateStore stateStore,
        IEnumerable<string> extensionIds,
        Action? stateChanged = null,
        ExtensionPackageManager? packageManager = null,
        IEnumerable<string>? commandPackIds = null,
        CommandPackLoader? commandPackLoader = null,
        Func<string, Task<bool>>? confirmPackReplacement = null,
        Func<string, IReadOnlySet<string>>? existingCommandNames = null)
    {
        _stateStore = stateStore;
        _extensionIds = extensionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _commandPackIds = (commandPackIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _componentIds = _extensionIds.Concat(_commandPackIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _stateChanged = stateChanged;
        _packageManager = packageManager ?? new ExtensionPackageManager();
        _commandPackLoader = commandPackLoader ?? new CommandPackLoader(stateStore);
        _confirmPackReplacement = confirmPackReplacement;
        _existingCommandNames = existingCommandNames;
    }

    public string Kind => "extension";

    public static CommandDefinition Definition { get; } = new(
            "extension",
            "system",
            "Manage installed extensions and command packs",
            [
                new CommandActionDefinition(
                    "install",
                    "List available packages or install an extension from a ZIP package or the profile package inbox",
                    "extension install <id|zip-path>",
                    "extension install",
                    [new CommandArgumentDefinition(
                        "zip-path",
                        Ref: ExtensionPackageNamesArgumentValueProvider.Reference,
                        ListMode: ListSelectionMode.Multiple,
                        AllowCustomValues: true)]),
                new CommandActionDefinition(
                    "pack-install",
                    "Install a command pack from a JSON manifest",
                    "extension pack-install <json-path>",
                    "extension pack-install",
                    [new CommandArgumentDefinition("json-path")]),
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
                    "Enable an extension or command pack immediately",
                    "extension enable <id>",
                    "extension enable",
                    [new CommandArgumentDefinition(
                        "id",
                        Ref: InstalledComponentIdsArgumentValueProvider.EnableReference,
                        ListMode: ListSelectionMode.Multiple)]),
                new CommandActionDefinition(
                    "disable",
                    "Disable an extension or command pack immediately",
                    "extension disable <id>",
                    "extension disable",
                    [new CommandArgumentDefinition(
                        "id",
                        Ref: InstalledComponentIdsArgumentValueProvider.DisableReference,
                        ListMode: ListSelectionMode.Multiple)])
            ]) { Handler = "extension" };

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
                $"{extension.Id} | {extension.Version} [{(stateStore.IsEnabled(extension.Id) ? "enabled" : "disabled")}]"
                + (extension.LoadError is null ? string.Empty : $" [failed to load: {extension.LoadError}]")));
    }

    public static string FormatList(
        IEnumerable<LoadedExtensionInfo> extensions,
        IEnumerable<LoadedCommandPackInfo> commandPacks)
    {
        var entries = extensions
            .Select(extension => (extension.Id, Version: extension.Version, Type: "extension", extension.Enabled, LoadError: (string?)extension.LoadError))
            .Concat(commandPacks.Select(pack => (pack.Id, Version: "manifest", Type: "pack", pack.Enabled, LoadError: (string?)null)))
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (entries.Length == 0)
        {
            return "No extensions or command packs installed.";
        }

        return string.Join(
            Environment.NewLine,
            entries.Select(entry =>
                $"{entry.Id} | {entry.Version} [{entry.Type}] [{(entry.Enabled ? "enabled" : "disabled")}]"
                + (entry.LoadError is null ? string.Empty : $" [failed to load: {entry.LoadError}]")));
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (string.Equals(command.Action.Name, "install", StringComparison.OrdinalIgnoreCase))
        {
            return command.Arguments.TryGetValue("zip-path", out var packageReference)
                ? Install(packageReference)
                : ListAvailablePackages();
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
            return new CommandExecutionResult(false, "At least one component ID is required.");
        }

        var enabled = string.Equals(command.Action.Name, "enable", StringComparison.OrdinalIgnoreCase);
        var state = enabled ? "enabled" : "disabled";
        var componentNames = FormatComponentNames(ids);
        var unknownId = ids.FirstOrDefault(id => !_componentIds.Contains(id));
        if (unknownId is not null)
        {
            return new CommandExecutionResult(false, $"Installed component '{unknownId}' was not found.");
        }

        var disabledIds = _stateStore.GetDisabledExtensionIds();
        var previousStates = ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, id => !disabledIds.Contains(id), StringComparer.OrdinalIgnoreCase);
        if (previousStates.Values.All(previousState => previousState == enabled))
        {
            var verb = ids.Length == 1 ? "is" : "are";
            return new CommandExecutionResult(true, $"{componentNames} {verb} already {state}.");
        }

        try
        {
            _stateStore.SetEnabled(ids, enabled);
            _stateChanged?.Invoke();
            return new CommandExecutionResult(true, $"{componentNames} {state}.");
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
                    $"Unable to update {componentNames}: {exception.Message} State rollback also failed: {rollbackException.Message}");
            }

            return new CommandExecutionResult(false, $"Unable to update {componentNames}: {exception.Message}");
        }
    }

    public async Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command)
    {
        if (!string.Equals(command.Action.Name, "pack-install", StringComparison.OrdinalIgnoreCase))
        {
            return Execute(command);
        }

        var installedPackPath = string.Empty;
        var previousManifest = Array.Empty<byte>();
        var hadPreviousManifest = false;
        try
        {
            var sourcePath = command.Arguments["json-path"];
            var id = CommandPackLoader.ValidateManifestFile(sourcePath);
            var destination = _commandPackLoader.GetInstalledPackPath(id);
            var replaceExisting = false;
            var hasImportPolicy = command.Arguments.TryGetValue("conflict-policy", out var conflictPolicy);
            var overwriteExistingCommands = string.Equals(conflictPolicy, "overwrite", StringComparison.OrdinalIgnoreCase);
            if (System.IO.File.Exists(destination))
            {
                if (!hasImportPolicy && (_confirmPackReplacement is null || !await _confirmPackReplacement(id)))
                {
                    return new CommandExecutionResult(false, $"Import of command pack '{id}' was cancelled.");
                }

                replaceExisting = true;
                previousManifest = System.IO.File.ReadAllBytes(destination);
                hadPreviousManifest = true;
            }

            installedPackPath = destination;
            var importResult = _commandPackLoader.ImportPack(
                sourcePath,
                _existingCommandNames?.Invoke(id) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                replaceExisting,
                overwriteExistingCommands,
                replacePackContents: replaceExisting);
            _stateChanged?.Invoke();
            var skippedMessage = importResult.SkippedCommands.Count == 0
                ? string.Empty
                : $" Skipped duplicate command(s): {string.Join(", ", importResult.SkippedCommands)}.";
            var overwrittenMessage = overwriteExistingCommands && replaceExisting
                ? " Existing commands in this pack were overwritten."
                : string.Empty;
            return new CommandExecutionResult(true, $"Command pack '{id}' installed.{overwrittenMessage}{skippedMessage}");
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrEmpty(installedPackPath))
            {
                try
                {
                    if (hadPreviousManifest)
                    {
                        System.IO.File.WriteAllBytes(installedPackPath, previousManifest);
                    }
                    else if (System.IO.File.Exists(installedPackPath))
                    {
                        System.IO.File.Delete(installedPackPath);
                    }
                }
                catch (Exception rollbackException)
                {
                    return new CommandExecutionResult(
                        false,
                        $"Unable to install command pack: {exception.Message} Rollback also failed: {rollbackException.Message}");
                }
            }

            return new CommandExecutionResult(false, $"Unable to install command pack: {exception.Message}");
        }
    }

    private CommandExecutionResult Install(string packagePath)
    {
        var packageReferences = packagePath.Split(',').Select(reference => reference.Trim()).ToArray();
        var previousEnabledStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var extensionIds = _packageManager.InstallPackages(packageReferences, installedIds =>
            {
                foreach (var installedId in installedIds)
                {
                    previousEnabledStates.Add(installedId, _stateStore.IsEnabled(installedId));
                }

                _stateStore.SetEnabled(installedIds, enabled: true);
                _stateChanged?.Invoke();
            });
            var installedLabel = extensionIds.Count == 1
                ? $"Extension '{extensionIds[0]}'"
                : $"Extensions '{string.Join("', '", extensionIds)}'";
            return new CommandExecutionResult(true, $"{installedLabel} installed.");
        }
        catch (Exception exception)
        {
            if (previousEnabledStates.Count > 0)
            {
                try
                {
                    _stateStore.SetEnabled(previousEnabledStates);
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

    public CommandExecutionResult ListAvailablePackages()
    {
        var packageNames = _packageManager.GetAvailablePackageNames();
        if (packageNames.Count == 0)
        {
            return new CommandExecutionResult(true, "No extension packages are available.")
            {
                IsValueOutput = true
            };
        }

        return new CommandExecutionResult(
            true,
            "Available extension packages:" + Environment.NewLine
            + string.Join(Environment.NewLine, packageNames.Select(packageName => $"  {packageName}")))
        {
            IsValueOutput = true
        };
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

    private string FormatComponentNames(IReadOnlyList<string> ids)
    {
        if (ids.All(_extensionIds.Contains))
        {
            return FormatExtensionNames(ids);
        }

        if (ids.All(_commandPackIds.Contains))
        {
            return ids.Count == 1
                ? $"Command pack '{ids[0]}'"
                : $"Command packs '{string.Join("', '", ids)}'";
        }

        return ids.Count == 1
            ? $"Installed component '{ids[0]}'"
            : $"Installed components '{string.Join("', '", ids)}'";
    }
}
