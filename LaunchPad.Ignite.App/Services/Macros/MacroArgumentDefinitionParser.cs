using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services.Macros;

public static class MacroArgumentDefinitionParser
{
    public static bool TryParse(
        string text,
        out IReadOnlyList<MacroArgumentDefinition> arguments,
        out string? error)
    {
        var parsed = new List<MacroArgumentDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var declaration = parts[0].Split(':', 2);
            if (declaration.Length != 2 || string.IsNullOrWhiteSpace(declaration[0]))
            {
                arguments = [];
                error = $"Invalid argument declaration '{line.Trim()}'. Use name:type.";
                return false;
            }

            if (!TryParseType(declaration[1], out var type))
            {
                arguments = [];
                error = $"Unsupported macro argument type '{declaration[1]}'.";
                return false;
            }

            var remaining = parts.Skip(1).ToArray();
            var listName = type == MacroArgumentType.List && remaining.Length > 0 ? remaining[0] : null;
            if (type == MacroArgumentType.List && string.IsNullOrWhiteSpace(listName))
            {
                arguments = [];
                error = $"List argument '{declaration[0]}' must specify a list name after '{parts[0]}'.";
                return false;
            }

            var modeToken = remaining.LastOrDefault(token =>
                string.Equals(token, "single", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "multiple", StringComparison.OrdinalIgnoreCase));
            var mode = modeToken is not null
                && string.Equals(modeToken, "single", StringComparison.OrdinalIgnoreCase)
                    ? ListSelectionMode.Single
                    : ListSelectionMode.Multiple;
            var validOptions = type switch
            {
                MacroArgumentType.List => remaining.Length <= 2
                    && (remaining.Length < 2 || modeToken is not null),
                MacroArgumentType.SecretList or MacroArgumentType.ValueList => remaining.Length <= 1
                    && (remaining.Length == 0 || modeToken is not null),
                _ => remaining.Length == 0
            };
            if (!validOptions)
            {
                arguments = [];
                error = $"Invalid options for macro argument '{declaration[0]}'.";
                return false;
            }

            if (!names.Add(declaration[0]))
            {
                arguments = [];
                error = $"Macro argument '{declaration[0]}' is declared more than once.";
                return false;
            }

            parsed.Add(new MacroArgumentDefinition(declaration[0], type, listName, mode));
        }

        arguments = parsed;
        error = null;
        return true;
    }

    private static bool TryParseType(string value, out MacroArgumentType type)
    {
        type = value.ToLowerInvariant() switch
        {
            "value" => MacroArgumentType.Value,
            "variable" => MacroArgumentType.Variable,
            "list" => MacroArgumentType.List,
            "secretlist" => MacroArgumentType.SecretList,
            "valuelist" => MacroArgumentType.ValueList,
            _ => (MacroArgumentType)(-1)
        };
        return Enum.IsDefined(type);
    }
}
