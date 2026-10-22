using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

public static class CommandMetadataFilterSuggestionResolver
{
    public static IReadOnlyList<string> Resolve(
        CommandSchema schema,
        IReadOnlyDictionary<string, string> commandPackSources,
        IReadOnlyDictionary<string, string> commandSources,
        CommandMetadataFilterInput filterInput)
    {
        if (filterInput.IsEditingFieldName)
        {
            return new[] { "kind", "pack", "extension" }
                .Where(field => field.StartsWith(filterInput.FieldPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var filteredCommands = schema.Commands
            .Select(command =>
            {
                var packId = commandPackSources.TryGetValue(command.QualifiedName, out var sourcePackId)
                    ? sourcePackId
                    : null;
                var extensionId = commandSources.TryGetValue(command.QualifiedName, out var sourceExtensionId)
                    ? sourceExtensionId
                    : null;
                return (Command: command, PackId: packId, ExtensionId: extensionId);
            })
            .Where(candidate => filterInput.CompletedFilter?.Matches(
                candidate.Command,
                candidate.PackId,
                candidate.ExtensionId) != false);

        IEnumerable<string> values = filterInput.EditingField switch
        {
            "pack" => filteredCommands
                .Where(candidate => candidate.PackId is not null)
                .Select(candidate => candidate.PackId!),
            "kind" => filteredCommands.Select(candidate => candidate.Command.Kind),
            "extension" => filteredCommands
                .Where(candidate => candidate.ExtensionId is not null)
                .Select(candidate => candidate.ExtensionId!),
            _ => Array.Empty<string>()
        };

        return values
            .Where(value => value.StartsWith(filterInput.ValuePrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}