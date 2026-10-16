using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public enum DefinitionManagementStatus
{
    NotManagementCommand,
    Valid,
    Invalid
}

public sealed record DefinitionManagementRequest(
    string Entity,
    string Operation,
    string Name,
    string? Kind = null,
    string? Target = null,
    string? Description = null,
    IReadOnlyList<CommandArgumentDefinition>? Arguments = null,
    IReadOnlyList<string>? TargetArguments = null,
    IReadOnlyList<string>? Values = null,
    string? Default = null,
    bool HasTarget = false,
    bool HasDescription = false,
    bool HasArguments = false,
    bool HasTargetArguments = false,
    bool HasValues = false,
    bool HasDefault = false,
    string? KeyboardShortcut = null,
    bool HasKeyboardShortcut = false,
    bool ClearKeyboardShortcut = false);

public sealed record DefinitionManagementParseResult(
    DefinitionManagementStatus Status,
    DefinitionManagementRequest? Request = null,
    string? Error = null);

public sealed record DefinitionManagementHelp(
    string Usage,
    string Example,
    IReadOnlyList<string> Suggestions);

public sealed class DefinitionManagementParser
{
    private readonly CommandLineTokenizer _tokenizer = new();

    public DefinitionManagementHelp GetHelp(string input)
    {
        var tokens = _tokenizer.Tokenize(input).Select(token => token.Value).ToArray();
        var suggestions = GetSuggestions(input, tokens);
        var entity = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : string.Empty;
        var operation = tokens.Length > 2 ? tokens[2].ToLowerInvariant() : string.Empty;
        var kind = tokens.Length > 3 ? tokens[3].ToLowerInvariant() : string.Empty;

        if (tokens.Length <= 1)
        {
            return new DefinitionManagementHelp(
                "Usage: launchpad <command|list> <operation> ...",
                "Example: launchpad command list",
                suggestions);
        }

        if (tokens.Length <= 2
            && (tokens.Length == 1
                || !IsEntity(tokens[1], "command") && !IsEntity(tokens[1], "list")))
        {
            return new DefinitionManagementHelp(
                "Usage: launchpad <command|list> <operation> ...",
                "Example: launchpad command list",
                suggestions);
        }

        if (tokens.Length <= 2)
        {
            return entity == "list"
                ? new DefinitionManagementHelp("Usage: launchpad list <add|edit|remove|show|list> ...", "Example: launchpad list list", suggestions)
                : new DefinitionManagementHelp("Usage: launchpad command <add|edit|remove|show|list> ...", "Example: launchpad command list", suggestions);
        }

        if (tokens.Length == 3
            && !(operation is "add" or "edit" or "remove" or "show" or "list"))
        {
            return entity == "list"
                ? new DefinitionManagementHelp("Usage: launchpad list <add|edit|remove|show|list> ...", "Example: launchpad list list", suggestions)
                : new DefinitionManagementHelp("Usage: launchpad command <add|edit|remove|show|list> ...", "Example: launchpad command list", suggestions);
        }

        if (operation == "add" && entity == "command")
        {
            if (tokens.Length <= 3)
            {
                return new DefinitionManagementHelp(
                    "Usage: launchpad command add <url|shell|shortcut> <name> ...",
                    "Example: launchpad command add url weather --target \"https://example.test/{city}\"",
                    suggestions);
            }

            if (tokens.Length == 4 && !(kind is "url" or "shell" or "shortcut" or "enum"))
            {
                return new DefinitionManagementHelp(
                    entity == "command"
                        ? "Usage: launchpad command add <url|shell|shortcut> <name> ..."
                        : "Usage: launchpad list add enum <name> --values <value1,value2,...> --default <value>",
                    entity == "command"
                        ? "Example: launchpad command add url weather --target \"https://example.test/{city}\""
                        : "Example: launchpad list add enum weather:cities --values \"forecast,current\" --default forecast",
                    suggestions);
            }

            if (kind == "shortcut")
            {
                return new DefinitionManagementHelp(
                    "Usage: launchpad command add shortcut <name> --target <command-text> [--key <chord>] [--description <text>]",
                    "Example: launchpad command add shortcut wx --target \"weather forecast\" --key ctrl+alt+w",
                    suggestions);
            }

            var usage = kind == "shell"
                ? "Usage: launchpad command add shell <name> --target <executable> [--target-arg <template>] [--arg <name:required|optional[:options]>] [--description <text>]"
                : "Usage: launchpad command add url <name> --target <url> [--arg <name:required|optional[:options]>] [--description <text>]";
            return new DefinitionManagementHelp(usage,
                "Example: launchpad command add url weather --target \"https://example.test/{city}\" --arg city:required",
                suggestions);
        }

        if (operation == "edit" && entity == "command")
        {
            return new DefinitionManagementHelp(
                "Usage: launchpad command edit <name> [--target <target>] [--arg <spec>] [--target-arg <template>] [--description <text>] [--key <chord>|--clear-key]",
                "Example: launchpad command edit wx --key ctrl+alt+w",
                suggestions);
        }

        if (operation == "add" && entity == "list")
        {
            return new DefinitionManagementHelp(
                "Usage: launchpad list add enum <name> --values <value1,value2,...> --default <value>",
                "Example: launchpad list add enum weather:cities --values \"forecast,current\" --default forecast",
                suggestions);
        }

        if (operation == "edit" && entity == "list")
        {
            return new DefinitionManagementHelp(
                "Usage: launchpad list edit <name> [--add <value>] [--remove <value>] [--values <value1,value2,...>] [--default <value>]",
                "Example: launchpad list edit weather:cities --add current --default current",
                suggestions);
        }

        var entityName = entity == "list" ? "list" : "command";
        var operationName = operation is "show" or "remove" ? operation : "list";
        var nameUsage = operationName == "list" ? string.Empty : " <name>";
        return new DefinitionManagementHelp(
            $"Usage: launchpad {entityName} {operationName}{nameUsage}",
            $"Example: launchpad {entityName} {operationName}{(nameUsage.Length == 0 ? string.Empty : " weather")}",
            suggestions);
    }

    private IReadOnlyList<string> GetSuggestions(string input, IReadOnlyList<string> tokens)
    {
        var candidates = new List<string>();
        var hasTrailingSpace = input.Length > 0 && char.IsWhiteSpace(input[^1]);
        var entity = tokens.Count > 1 ? tokens[1].ToLowerInvariant() : string.Empty;
        var operation = tokens.Count > 2 ? tokens[2].ToLowerInvariant() : string.Empty;
        if (tokens.Count <= 1)
        {
            candidates.AddRange(["launchpad command ", "launchpad list "]);
        }
        else if (tokens.Count == 2 && !hasTrailingSpace
            && !entity.Equals("command", StringComparison.OrdinalIgnoreCase)
            && !entity.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            candidates.AddRange(["launchpad command ", "launchpad list "]);
        }
        else if (tokens.Count == 2 && (entity == "command" || entity == "list"))
        {
            var prefix = $"launchpad {entity} ";
            var operations = new[] { "add", "edit", "remove", "show", "list" };
            candidates.AddRange(operations.Select(item => prefix + item + " "));
        }
        else if (tokens.Count == 3 && hasTrailingSpace && operation == "add")
        {
            candidates.AddRange(entity == "command"
                ? new[] { "launchpad command add url ", "launchpad command add shell ", "launchpad command add shortcut " }
                : new[] { "launchpad list add enum " });
        }
        else if (tokens.Count == 3 && !hasTrailingSpace)
        {
            var operations = new[] { "add", "edit", "remove", "show", "list" };
            var prefix = $"launchpad {entity} ";
            candidates.AddRange(operations.Select(item => prefix + item + " "));
            if (operation.Equals("add", StringComparison.OrdinalIgnoreCase)
                || "add".StartsWith(operation, StringComparison.OrdinalIgnoreCase))
            {
                candidates.AddRange(entity == "command"
                    ? new[] { "launchpad command add url ", "launchpad command add shell ", "launchpad command add shortcut " }
                    : new[] { "launchpad list add enum " });
            }
        }
        else if (tokens.Count == 4 && !hasTrailingSpace && entity == "command" && operation == "add")
        {
            candidates.AddRange(new[] { "url", "shell", "shortcut" }
                .Select(kind => $"launchpad command add {kind} "));
        }
        else if (hasTrailingSpace && tokens.Count >= 4)
        {
            var trimmedInput = input.TrimEnd();
            if (entity == "command" && operation == "add"
                && tokens.Count == 5 && (tokens[3] is "url" or "shell"))
            {
                candidates.AddRange(new[] { "--target", "--arg", "--description", "--target-arg" }
                    .Select(option => $"{trimmedInput} {option} "));
            }
            else if (entity == "command" && operation == "add"
                && tokens.Count == 5 && tokens[3] == "shortcut")
            {
                candidates.AddRange(new[] { "--target", "--key", "--description" }
                    .Select(option => $"{trimmedInput} {option} "));
            }
            else if (entity == "command" && operation == "edit" && tokens.Count >= 4)
            {
                candidates.AddRange(new[] { "--target", "--arg", "--description", "--target-arg", "--key", "--clear-key" }
                    .Select(option => $"{trimmedInput} {option} "));
            }
            else if (entity == "list" && operation == "add" && tokens.Count >= 5)
            {
                candidates.AddRange(new[] { "--values", "--default" }
                    .Select(option => $"{trimmedInput} {option} "));
            }
            else if (entity == "list" && operation == "edit" && tokens.Count >= 4)
            {
                candidates.AddRange(new[] { "--add", "--remove", "--values", "--default" }
                    .Select(option => $"{trimmedInput} {option} "));
            }
        }

        return candidates
            .Where(candidate => candidate.Length > input.Length
                && candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    public bool IsManagementCommand(string input)
    {
        var tokens = _tokenizer.Tokenize(input);
        return tokens.Count > 0
            && tokens[0].Value.Equals("launchpad", StringComparison.OrdinalIgnoreCase)
            && ((tokens.Count == 1 && _tokenizer.EndsWithWhitespace(input))
                || (tokens.Count > 1
                    && ("command".StartsWith(tokens[1].Value, StringComparison.OrdinalIgnoreCase)
                        || "list".StartsWith(tokens[1].Value, StringComparison.OrdinalIgnoreCase))));
    }

    public bool ShouldAppendSeparatorOnTab(string input)
    {
        var tokens = _tokenizer.Tokenize(input);
        return !_tokenizer.EndsWithWhitespace(input)
            && tokens.Count == 2
            && tokens[0].Value.Equals("launchpad", StringComparison.OrdinalIgnoreCase)
            && (tokens[1].Value.Equals("command", StringComparison.OrdinalIgnoreCase)
                || tokens[1].Value.Equals("list", StringComparison.OrdinalIgnoreCase));
    }

    public DefinitionManagementParseResult Parse(string input)
    {
        var tokens = _tokenizer.Tokenize(input).Select(token => token.Value).ToArray();
        if (tokens.Length == 0 || !string.Equals(tokens[0], "launchpad", StringComparison.OrdinalIgnoreCase))
        {
            return new DefinitionManagementParseResult(DefinitionManagementStatus.NotManagementCommand);
        }

        if (tokens.Length < 2 || (!IsEntity(tokens[1], "command") && !IsEntity(tokens[1], "list")))
        {
            return new DefinitionManagementParseResult(DefinitionManagementStatus.NotManagementCommand);
        }

        var entity = tokens[1].ToLowerInvariant();
        if (tokens.Length < 3)
        {
            return Invalid("Expected an operation: add, edit, remove, list, or show.");
        }

        var operation = tokens[2].ToLowerInvariant();
        var index = 3;
        string? kind = null;
        if (operation == "add" && entity == "command")
        {
            if (index >= tokens.Length || !IsCommandKind(tokens[index]))
            {
                return Invalid("Command add requires a supported kind: url, shell, or shortcut.");
            }

            kind = tokens[index++].ToLowerInvariant();
        }
        else if (operation == "add" && entity == "list")
        {
            if (index >= tokens.Length || tokens[index] != "enum")
            {
                return Invalid("List add currently supports only the enum type.");
            }

            kind = tokens[index++];
        }

        var requiresName = operation is "add" or "edit" or "remove" or "show";
        var name = requiresName && index < tokens.Length ? tokens[index++] : string.Empty;
        if (requiresName && string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A name is required.");
        }

        if (operation is not ("add" or "edit" or "remove" or "show" or "list"))
        {
            return Invalid($"Unsupported {entity} operation '{operation}'.");
        }

        if ((operation is "remove" or "show" or "list") && index < tokens.Length)
        {
            return Invalid($"The {operation} operation does not accept options.");
        }

        string? target = null;
        string? description = null;
        string? defaultValue = null;
        var arguments = new List<CommandArgumentDefinition>();
        var targetArguments = new List<string>();
        var values = new List<string>();
        var hasTarget = false;
        var hasDescription = false;
        var hasArguments = false;
        var hasTargetArguments = false;
        var hasValues = false;
        var hasDefault = false;
        string? keyboardShortcut = null;
        var hasKeyboardShortcut = false;
        var clearKeyboardShortcut = false;

        if (operation == "list" && entity == "command" && index < tokens.Length)
        {
            return Invalid("Command list does not accept a name or options.");
        }

        while (index < tokens.Length)
        {
            var option = tokens[index++];
            if (entity == "command")
            {
                switch (option)
                {
                    case "--target":
                        if (!TryReadValue(tokens, ref index, out target)) return Missing(option);
                        hasTarget = true;
                        break;
                    case "--description":
                        if (!TryReadValue(tokens, ref index, out description)) return Missing(option);
                        hasDescription = true;
                        break;
                    case "--arg":
                        if (!TryReadValue(tokens, ref index, out var argumentSpec)) return Missing(option);
                        if (!TryParseArgument(argumentSpec!, out var argument, out var error)) return Invalid(error!);
                        arguments.Add(argument!);
                        hasArguments = true;
                        break;
                    case "--target-arg":
                        if (!TryReadValue(tokens, ref index, out var targetArgument)) return Missing(option);
                        targetArguments.Add(targetArgument!);
                        hasTargetArguments = true;
                        break;
                    case "--key":
                        if (!TryReadValue(tokens, ref index, out var shortcutValue)) return Missing(option);
                        if (!KeyboardShortcutChord.TryParse(shortcutValue, out var chord))
                        {
                            return Invalid($"Invalid keyboard shortcut '{shortcutValue}'. Use a modifier and key, such as ctrl+alt+w.");
                        }
                        keyboardShortcut = chord.ToString();
                        hasKeyboardShortcut = true;
                        break;
                    case "--clear-key":
                        clearKeyboardShortcut = true;
                        break;
                    default:
                        return Invalid($"Unknown command option '{option}'.");
                }
            }
            else
            {
                switch (option)
                {
                    case "--values":
                        if (!TryReadValue(tokens, ref index, out var csv)) return Missing(option);
                        if (!TryParseCsv(csv!, out var parsedValues, out var csvError)) return Invalid(csvError!);
                        values.AddRange(parsedValues!);
                        hasValues = true;
                        break;
                    case "--add":
                        if (!TryReadValue(tokens, ref index, out var addValue)) return Missing(option);
                        values.Add(addValue!);
                        break;
                    case "--remove":
                        if (!TryReadValue(tokens, ref index, out var removeValue)) return Missing(option);
                        values.Add("\u001f" + removeValue);
                        break;
                    case "--default":
                        if (!TryReadValue(tokens, ref index, out defaultValue)) return Missing(option);
                        hasDefault = true;
                        break;
                    default:
                        return Invalid($"Unknown list option '{option}'.");
                }
            }
        }

        if (operation == "add" && entity == "command" && !hasTarget)
        {
            return Invalid("Command add requires --target.");
        }
        if (operation == "add" && entity == "list" && !hasValues)
        {
            return Invalid("List add requires --values.");
        }
        if (operation == "edit" && !(hasTarget || hasDescription || hasArguments || hasTargetArguments || hasValues || hasDefault || values.Count > 0 || hasKeyboardShortcut || clearKeyboardShortcut))
        {
            return Invalid("Edit requires at least one property option.");
        }
        if (hasKeyboardShortcut && clearKeyboardShortcut)
        {
            return Invalid("Use either --key or --clear-key, not both.");
        }
        if (clearKeyboardShortcut && operation != "edit")
        {
            return Invalid("--clear-key is supported only when editing a command.");
        }
        if (entity == "list" && operation == "add" && !hasDefault)
        {
            return Invalid("List add requires --default.");
        }
        if (entity == "command" && operation == "add" && hasTargetArguments && !string.Equals(kind, "shell", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("--target-arg is supported only for shell commands.");
        }
        if (entity == "command" && operation == "add" && string.Equals(kind, "shortcut", StringComparison.OrdinalIgnoreCase)
            && (hasArguments || hasTargetArguments))
        {
            return Invalid("Shortcut commands do not accept --arg or --target-arg.");
        }
        if (entity == "command" && operation == "add" && hasKeyboardShortcut
            && !string.Equals(kind, "shortcut", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("--key is supported only when adding shortcut commands.");
        }
        if (entity == "list" && hasValues && values.Any(value => value.StartsWith("\u001f", StringComparison.Ordinal)))
        {
            return Invalid("Use either --values or incremental --add/--remove options, not both.");
        }

        return new DefinitionManagementParseResult(
            DefinitionManagementStatus.Valid,
            new DefinitionManagementRequest(entity, operation, name, kind, target, description, arguments,
                targetArguments, values, defaultValue, hasTarget, hasDescription, hasArguments,
                hasTargetArguments, hasValues, hasDefault, keyboardShortcut, hasKeyboardShortcut,
                clearKeyboardShortcut));
    }

    private static bool IsCommandKind(string value)
        => value.Equals("url", StringComparison.OrdinalIgnoreCase)
            || value.Equals("shell", StringComparison.OrdinalIgnoreCase)
            || value.Equals("shortcut", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseArgument(string spec, out CommandArgumentDefinition? argument, out string? error)
    {
        argument = null;
        error = null;
        var parts = spec.Split(':');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            error = "Argument syntax is name:required|optional[:ref=enum:listName][:type=csv].";
            return false;
        }

        var optional = parts[1].Equals("optional", StringComparison.OrdinalIgnoreCase);
        if (!optional && !parts[1].Equals("required", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Argument '{parts[0]}' must specify required or optional.";
            return false;
        }

        string? reference = null;
        string? inputType = null;
        var listMode = ListSelectionMode.Multiple;
        var allowCustomValues = false;
        for (var index = 2; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith("ref=", StringComparison.OrdinalIgnoreCase))
            {
                var referenceParts = new List<string> { part[4..] };
                while (index + 1 < parts.Length && !parts[index + 1].StartsWith("type=", StringComparison.OrdinalIgnoreCase))
                {
                    referenceParts.Add(parts[++index]);
                }
                reference = string.Join(':', referenceParts);
            }
            else if (part.StartsWith("type=", StringComparison.OrdinalIgnoreCase)) inputType = part[5..];
            else if (part.Equals("single", StringComparison.OrdinalIgnoreCase)) listMode = ListSelectionMode.Single;
            else if (part.Equals("multiple", StringComparison.OrdinalIgnoreCase)) listMode = ListSelectionMode.Multiple;
            else if (part.Equals("custom", StringComparison.OrdinalIgnoreCase)) allowCustomValues = true;
            else
            {
                error = $"Unsupported argument option '{part}'.";
                return false;
            }
        }

        argument = new CommandArgumentDefinition(parts[0], optional, reference, inputType, listMode, allowCustomValues);
        return true;
    }

    private static bool TryParseCsv(string input, out IReadOnlyList<string>? values, out string? error)
    {
        var parsed = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character == '"')
            {
                if (quoted && index + 1 < input.Length && input[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                parsed.Add(value.ToString().Trim());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        if (quoted)
        {
            values = null;
            error = "CSV values contain an unclosed quote.";
            return false;
        }

        parsed.Add(value.ToString().Trim());
        if (parsed.Any(string.IsNullOrWhiteSpace))
        {
            values = null;
            error = "CSV values cannot be empty.";
            return false;
        }

        values = parsed;
        error = null;
        return true;
    }

    private static bool TryReadValue(string[] tokens, ref int index, out string? value)
    {
        value = null;
        if (index >= tokens.Length || (tokens[index].StartsWith("--", StringComparison.Ordinal)
            || tokens[index].StartsWith("-", StringComparison.Ordinal))) return false;
        value = tokens[index++];
        return true;
    }

    private static bool IsEntity(string token, string entity) => token.Equals(entity, StringComparison.OrdinalIgnoreCase);
    private static DefinitionManagementParseResult Invalid(string error) => new(DefinitionManagementStatus.Invalid, Error: error);
    private static DefinitionManagementParseResult Missing(string option) => Invalid($"Option '{option}' requires a value.");
}
