using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record CommandOwnershipItem(
    CommandDefinition Command,
    string OwnerId,
    string OwnerName,
    bool IsUserCommand)
{
    public string QualifiedName => Command.QualifiedName;
}

public sealed record CommandOwnershipKey(string OwnerId, string QualifiedName);

public sealed record CommandMovePreview(
    IReadOnlyList<CommandOwnershipItem> Commands,
    IReadOnlyList<CommandOwnershipItem> CommandsToMove,
    string? DestinationPackId,
    IReadOnlyList<string> Conflicts)
{
    public bool CanCommit => CommandsToMove.Count > 0 && Conflicts.Count == 0;
}

public sealed record CommandMoveResult(bool Succeeded, string Message, IReadOnlyList<string> Conflicts);

public sealed record CommandPackExportPlan(
    CommandPackManifest Manifest,
    CommandPackDependencies Dependencies,
    IReadOnlyList<string> ExportedCommandNames);

public sealed class CommandOwnershipManager
{
    public const string UserOwnerId = "user";

    private readonly CommandSchemaLoader _userSchemaLoader;
    private readonly CommandPackLoader _packLoader;
    private readonly ExtensionStateStore _stateStore;
    private readonly IReadOnlyList<LoadedCommandPackInfo> _packs;
    private readonly HashSet<string> _reservedCommandNames;
    private readonly Action _runtimeRefresh;
    private readonly CommandPackDependencyResolver _dependencyResolver = new();

    public CommandOwnershipManager(
        CommandSchemaLoader userSchemaLoader,
        CommandPackLoader packLoader,
        ExtensionStateStore stateStore,
        IEnumerable<LoadedCommandPackInfo> packs,
        IEnumerable<string> reservedCommandNames,
        Action runtimeRefresh)
    {
        _userSchemaLoader = userSchemaLoader;
        _packLoader = packLoader;
        _stateStore = stateStore;
        _packs = packs.ToArray();
        _reservedCommandNames = reservedCommandNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _runtimeRefresh = runtimeRefresh;
    }

    public IReadOnlyList<LoadedCommandPackInfo> EnabledPacks
        => _packs.Where(pack => pack.Enabled && _stateStore.IsEnabled(pack.Id))
            .OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<CommandOwnershipItem> GetEligibleCommands()
    {
        var allPackCommandNames = _packs.SelectMany(pack => pack.Commands)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = _userSchemaLoader.ReadUserSchemaForTransfer().Commands
            .Where(command => !allPackCommandNames.Contains(command.QualifiedName))
            .Select(command => new CommandOwnershipItem(command, UserOwnerId, "User commands", true))
            .ToList();

        foreach (var pack in EnabledPacks)
        {
            var manifest = _packLoader.ReadInstalledManifest(pack.Id);
            items.AddRange(manifest.Schema.Commands.Select(command =>
            {
                var effectiveCommand = command with { Namespace = command.Namespace ?? manifest.Namespace };
                return new CommandOwnershipItem(effectiveCommand, pack.Id, pack.Id, false);
            }));
        }

        return items.OrderBy(item => item.OwnerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CommandPackExportPlan CreateExportPlan(
        IEnumerable<CommandOwnershipKey> selection,
        string packId,
        string? namespacePrefix)
    {
        var inventory = GetEligibleCommands().ToDictionary(
            item => new CommandOwnershipKey(item.OwnerId, item.QualifiedName),
            item => item);
        var selectedItems = selection.Distinct().Select(key => inventory.TryGetValue(key, out var item)
            ? item
            : throw new InvalidOperationException($"Command '{key.QualifiedName}' is no longer available from '{key.OwnerId}'. Refresh the dialog and try again."))
            .ToArray();
        if (selectedItems.Length == 0)
        {
            throw new InvalidOperationException("Select at least one command to export.");
        }

        var userSchema = _userSchemaLoader.ReadUserSchemaForTransfer();
        var lists = userSchema.Lists.ToList();
        var variables = userSchema.Variables.ToList();
        foreach (var pack in EnabledPacks)
        {
            var manifest = _packLoader.ReadInstalledManifest(pack.Id);
            lists.AddRange(manifest.Schema.Lists);
            variables.AddRange(manifest.Schema.Variables.Select(variable => variable with
            {
                Namespace = variable.Namespace ?? manifest.Namespace
            }));
        }

        var dependencies = _dependencyResolver.Resolve(
            selectedItems.Select(item => item.Command),
            lists,
            variables);
        var exportedCommands = selectedItems
            .Select(item => item.Command with { Namespace = null })
            .ToArray();
        var exportedVariables = dependencies.Variables
            .Select(variable => variable with { Namespace = variable.Namespace ?? string.Empty })
            .ToArray();
        var manifestToExport = new CommandPackManifest(
            packId,
            string.IsNullOrWhiteSpace(namespacePrefix) ? null : namespacePrefix.Trim(),
            new CommandSchema(dependencies.Lists, exportedCommands)
            {
                Variables = exportedVariables
            });
        _packLoader.ValidateInstalledManifest(manifestToExport);
        var exportedCommandNames = exportedCommands
            .Select(command => (command with { Namespace = command.Namespace ?? manifestToExport.Namespace }).QualifiedName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CommandPackExportPlan(manifestToExport, dependencies, exportedCommandNames);
    }

    public void WriteExportManifest(CommandPackExportPlan plan, string path)
        => _packLoader.WriteExportManifest(plan.Manifest, path);

    public CommandMovePreview PreviewMove(IEnumerable<CommandOwnershipKey> selection, string? destinationPackId)
    {
        var requested = selection.Distinct().ToArray();
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inventory = GetEligibleCommands();
        var byKey = inventory.ToDictionary(
            item => new CommandOwnershipKey(item.OwnerId, item.QualifiedName),
            item => item);
        var selectedItems = new List<CommandOwnershipItem>();
        foreach (var key in requested)
        {
            if (byKey.TryGetValue(key, out var item))
            {
                selectedItems.Add(item);
            }
            else
            {
                conflicts.Add($"{key.QualifiedName} is no longer available from '{key.OwnerId}'. Refresh the dialog and try again.");
            }
        }

        if (destinationPackId is not null && !EnabledPacks.Any(pack => string.Equals(pack.Id, destinationPackId, StringComparison.OrdinalIgnoreCase)))
        {
            conflicts.Add($"Destination pack '{destinationPackId}' is not enabled or no longer installed.");
        }

        var targetOwnerId = destinationPackId ?? UserOwnerId;
        var movingItems = selectedItems
            .Where(item => !string.Equals(item.OwnerId, targetOwnerId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var movingNames = movingItems.Select(item => item.QualifiedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingNames = inventory
            .Where(item => !movingItems.Any(selectedItem =>
                string.Equals(selectedItem.OwnerId, item.OwnerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(selectedItem.QualifiedName, item.QualifiedName, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.QualifiedName)
            .Concat(_reservedCommandNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in movingItems.GroupBy(item => item.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            conflicts.Add($"Multiple selected commands have the qualified name '{duplicate.Key}'.");
        }

        foreach (var item in movingItems)
        {
            if (remainingNames.Contains(item.QualifiedName))
            {
                conflicts.Add($"'{item.QualifiedName}' already exists at the destination or is reserved by another component.");
            }
        }

        return new CommandMovePreview(
            selectedItems,
            movingItems,
            destinationPackId,
            conflicts.OrderBy(conflict => conflict, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public CommandMoveResult MoveCommands(IEnumerable<CommandOwnershipKey> selection, string? destinationPackId)
    {
        var preview = PreviewMove(selection, destinationPackId);
        if (preview.Conflicts.Count > 0)
        {
            return new CommandMoveResult(false, string.Join(Environment.NewLine, preview.Conflicts), preview.Conflicts);
        }

        if (preview.CommandsToMove.Count == 0)
        {
            return new CommandMoveResult(true, "The selected commands already belong to the destination.", []);
        }

        var originalUserSchema = _userSchemaLoader.ReadUserSchemaForTransfer();
        var originalPacks = new Dictionary<string, CommandPackManifest>(StringComparer.OrdinalIgnoreCase);
        var userSchema = originalUserSchema;
        var packSchemas = new Dictionary<string, CommandPackManifest>(StringComparer.OrdinalIgnoreCase);
        var commandsMovingFromPacks = preview.CommandsToMove
            .Where(item => !item.IsUserCommand)
            .Select(item => item.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var packId in preview.CommandsToMove
            .Where(item => !item.IsUserCommand)
            .Select(item => item.OwnerId)
            .Concat(destinationPackId is null ? [] : [destinationPackId])
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manifest = _packLoader.ReadInstalledManifest(packId);
            originalPacks[packId] = manifest;
            packSchemas[packId] = manifest;
        }

        foreach (var item in preview.CommandsToMove)
        {
            var command = item.Command;
            if (item.IsUserCommand)
            {
                userSchema = userSchema with
                {
                    Commands = userSchema.Commands
                        .Where(existing => !string.Equals(existing.QualifiedName, item.QualifiedName, StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                };
            }
            else
            {
                var sourceManifest = packSchemas[item.OwnerId];
                packSchemas[item.OwnerId] = sourceManifest with
                {
                    Schema = sourceManifest.Schema with
                    {
                        Commands = sourceManifest.Schema.Commands
                            .Where(existing => !string.Equals(
                                (existing with { Namespace = existing.Namespace ?? sourceManifest.Namespace }).QualifiedName,
                                item.QualifiedName,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    }
                };
            }

            var commandPreservingIdentity = command with { Namespace = command.Namespace ?? string.Empty };
            if (destinationPackId is null)
            {
                userSchema = userSchema with { Commands = userSchema.Commands.Append(commandPreservingIdentity).ToArray() };
            }
            else
            {
                var destination = packSchemas[destinationPackId];
                packSchemas[destinationPackId] = destination with
                {
                    Schema = destination.Schema with
                    {
                        Commands = destination.Schema.Commands.Append(commandPreservingIdentity).ToArray()
                    }
                };
            }
        }

        try
        {
            _userSchemaLoader.ValidateUserSchemaForTransfer(userSchema, commandsMovingFromPacks);
            foreach (var manifest in packSchemas.Values)
            {
                _packLoader.ValidateInstalledManifest(manifest);
            }

            var changedPackIds = new List<string>();
            var deletedPackIds = new List<string>();
            var userWriteAttempted = false;
            try
            {
                foreach (var (packId, manifest) in packSchemas)
                {
                    changedPackIds.Add(packId);
                    if (manifest.Schema.Commands.Count == 0)
                    {
                        _packLoader.DeleteInstalledPack(packId);
                        deletedPackIds.Add(packId);
                    }
                    else
                    {
                        _packLoader.WriteInstalledManifest(manifest);
                    }
                }

                if (preview.CommandsToMove.Any(item => item.IsUserCommand) || destinationPackId is null)
                {
                    userWriteAttempted = true;
                    _userSchemaLoader.WriteUserSchemaForTransfer(userSchema, commandsMovingFromPacks);
                }

                _runtimeRefresh();
                return new CommandMoveResult(true, $"Moved {preview.CommandsToMove.Count} command(s).", []);
            }
            catch (Exception exception)
            {
                var rollbackErrors = new List<string>();
                foreach (var packId in deletedPackIds.AsEnumerable().Reverse())
                {
                    try
                    {
                        _packLoader.WriteInstalledManifest(originalPacks[packId]);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add($"Pack '{packId}': {rollbackException.Message}");
                    }
                }

                foreach (var packId in changedPackIds.AsEnumerable().Reverse())
                {
                    if (deletedPackIds.Contains(packId, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        _packLoader.WriteInstalledManifest(originalPacks[packId]);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add($"Pack '{packId}': {rollbackException.Message}");
                    }
                }

                if (userWriteAttempted)
                {
                    try
                    {
                        _userSchemaLoader.RestoreUserSchemaForTransfer(originalUserSchema);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add($"User commands: {rollbackException.Message}");
                    }
                }

                if (changedPackIds.Count > 0 || userWriteAttempted)
                {
                    try
                    {
                        _runtimeRefresh();
                    }
                    catch (Exception refreshException)
                    {
                        rollbackErrors.Add($"Runtime refresh: {refreshException.Message}");
                    }
                }

                var message = rollbackErrors.Count == 0
                    ? $"Move failed: {exception.Message}"
                    : $"Move failed: {exception.Message} Rollback issues: {string.Join("; ", rollbackErrors)}";
                return new CommandMoveResult(false, message, []);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            return new CommandMoveResult(false, $"Move could not be validated: {exception.Message}", []);
        }
    }
}
