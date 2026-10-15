using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

public sealed class CommandMetadataFilter
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> _valuesByField;

    private CommandMetadataFilter(Dictionary<string, HashSet<string>> valuesByField)
    {
        _valuesByField = valuesByField;
    }

    public bool Matches(CommandDefinition command, string? packId, string? extensionId = null)
        => _valuesByField.All(field => field.Key switch
        {
            "kind" => field.Value.Contains(command.Kind),
            "pack" => packId is not null && field.Value.Contains(packId),
            "extension" => extensionId is not null && field.Value.Contains(extensionId),
            _ => false
        });

    public static CommandMetadataFilterInput ParseInput(string input)
    {
        if (!input.StartsWith('>'))
        {
            return new(false, false, null, input, string.Empty, null, string.Empty, null, -1, false, string.Empty, -1);
        }

        var separatorIndex = input.IndexOfAny([' ', '\t', '\r', '\n']);
        var directiveEnd = separatorIndex < 0 ? input.Length : separatorIndex;
        var directive = input[1..directiveEnd];
        if (separatorIndex >= 0 && TryParse(directive, out var filter))
        {
            return new(
                true,
                true,
                filter,
                input[(separatorIndex + 1)..].TrimStart(),
                input[..(separatorIndex + 1)],
                null,
                string.Empty,
                null,
                -1,
                false,
                string.Empty,
                -1);
        }

        var lastCommaIndex = directive.LastIndexOf(',');
        var completedClauses = lastCommaIndex < 0 ? string.Empty : directive[..lastCommaIndex];
        var currentClause = directive[(lastCommaIndex + 1)..].Trim();
        CommandMetadataFilter? completedFilter = null;
        if (completedClauses.Length > 0 && !TryParse(completedClauses, out completedFilter!))
        {
            completedFilter = null;
        }

        var editingField = TryParseFieldValue(currentClause, out var field, out var valuePrefix)
            ? field
            : null;
        var editingValue = editingField is null ? string.Empty : valuePrefix;
        var editingFieldName = editingField is null && !currentClause.Contains(':');
        var fieldPrefix = editingFieldName ? currentClause : string.Empty;
        var editingFieldStart = editingFieldName
            ? 1 + lastCommaIndex + 1 + (directive[(lastCommaIndex + 1)..].Length - directive[(lastCommaIndex + 1)..].TrimStart().Length)
            : -1;
        var editingValueStart = editingField is null
            ? -1
            : 1 + lastCommaIndex + 1 + currentClause.IndexOf(':') + 1;
        return new(
            true,
            false,
            null,
            string.Empty,
            input[..directiveEnd],
            editingField,
            editingValue,
            completedFilter,
            editingValueStart,
            editingFieldName,
            fieldPrefix,
            editingFieldStart);
    }

    private static bool TryParseFieldValue(string clause, out string field, out string value)
    {
        var separatorIndex = clause.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex != clause.LastIndexOf(':'))
        {
            field = string.Empty;
            value = string.Empty;
            return false;
        }

        field = clause[..separatorIndex].Trim().ToLowerInvariant();
        value = clause[(separatorIndex + 1)..].Trim();
        return field is "kind" or "pack" or "extension";
    }

    public static bool TryParse(string value, out CommandMetadataFilter filter)
    {
        var valuesByField = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var clauses = value.Split(',', StringSplitOptions.TrimEntries);
        foreach (var clause in clauses)
        {
            var separatorIndex = clause.IndexOf(':');
            if (separatorIndex <= 0
                || separatorIndex == clause.Length - 1
                || separatorIndex != clause.LastIndexOf(':'))
            {
                filter = null!;
                return false;
            }

            var field = clause[..separatorIndex].Trim().ToLowerInvariant();
            var fieldValue = clause[(separatorIndex + 1)..].Trim();
            if (fieldValue.Length == 0 || field is not ("kind" or "pack" or "extension"))
            {
                filter = null!;
                return false;
            }

            if (!valuesByField.TryGetValue(field, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                valuesByField.Add(field, values);
            }

            values.Add(fieldValue);
        }

        filter = new CommandMetadataFilter(valuesByField);
        return valuesByField.Count > 0;
    }
}

public readonly record struct CommandMetadataFilterInput(
    bool IsFilterInput,
    bool IsComplete,
    CommandMetadataFilter? Filter,
    string CommandText,
    string FilterPrefix,
    string? EditingField,
    string ValuePrefix,
    CommandMetadataFilter? CompletedFilter,
    int EditingValueStart,
    bool IsEditingFieldName,
    string FieldPrefix,
    int EditingFieldStart);
