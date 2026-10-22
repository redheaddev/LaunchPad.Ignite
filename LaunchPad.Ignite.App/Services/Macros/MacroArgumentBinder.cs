using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services.Macros;

public sealed record MacroArgumentBindingResult(
    bool Succeeded,
    IReadOnlyDictionary<string, string> Arguments,
    string? Error = null);

public sealed class MacroArgumentBinder
{
    private readonly CommandLineTokenizer _tokenizer = new();
    private readonly IArgumentValueProvider _valueProvider;

    public MacroArgumentBinder(IArgumentValueProvider valueProvider)
    {
        _valueProvider = valueProvider;
    }

    public MacroArgumentBindingResult Bind(
        string input,
        MacroDefinition macro,
        IEnumerable<CommandVariableDefinition> variables)
    {
        var tokens = _tokenizer.Tokenize(input);
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nextPositionalIndex = 0;
        foreach (var token in tokens)
        {
            var equalsIndex = token.Value.IndexOf('=');
            if (equalsIndex >= 0)
            {
                if (equalsIndex == 0)
                {
                    return Failure("Named macro arguments must use the form name=value.");
                }

                var name = token.Value[..equalsIndex];
                if (!macro.Metadata.Arguments.Any(argument =>
                    string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return Failure($"Macro '{macro.Metadata.QualifiedName}' has no argument named '{name}'.");
                }

                var value = token.Value[(equalsIndex + 1)..];
                if (value.Length == 0)
                {
                    return Failure($"Macro argument '{name}' requires a value.");
                }

                if (!supplied.TryAdd(name, value))
                {
                    return Failure($"Macro argument '{name}' was supplied more than once.");
                }

                continue;
            }

            while (nextPositionalIndex < macro.Metadata.Arguments.Count
                && supplied.ContainsKey(macro.Metadata.Arguments[nextPositionalIndex].Name))
            {
                nextPositionalIndex++;
            }

            if (nextPositionalIndex >= macro.Metadata.Arguments.Count)
            {
                return Failure("Too many macro arguments were supplied.");
            }

            supplied.Add(macro.Metadata.Arguments[nextPositionalIndex].Name, token.Value);
            nextPositionalIndex++;
        }

        var missing = macro.Metadata.Arguments
            .FirstOrDefault(argument => !supplied.ContainsKey(argument.Name));
        if (missing is not null)
        {
            return Failure($"A value for macro argument '{missing.Name}' is required.");
        }

        var variableValues = variables.ToDictionary(
            variable => variable.QualifiedName,
            variable => variable.Value,
            StringComparer.OrdinalIgnoreCase);
        var bound = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in macro.Metadata.Arguments)
        {
            if (!TryPrepareValue(argument, supplied[argument.Name], variableValues, out var value, out var error))
            {
                return Failure(error!);
            }

            bound.Add(argument.Name, value);
        }

        return new MacroArgumentBindingResult(true, bound);
    }

    private bool TryPrepareValue(
        MacroArgumentDefinition argument,
        string suppliedValue,
        IReadOnlyDictionary<string, string> variables,
        out string value,
        out string? error)
    {
        value = suppliedValue;
        error = null;
        switch (argument.Type)
        {
            case MacroArgumentType.Value:
                return true;
            case MacroArgumentType.Variable:
                if (suppliedValue.Length < 2 || suppliedValue[0] != '$')
                {
                    error = $"Macro argument '{argument.Name}' must be a variable reference such as $project.";
                    return false;
                }

                var variableName = suppliedValue[1..];
                if (!variables.TryGetValue(variableName, out value!))
                {
                    error = $"Variable '${variableName}' was not found for macro argument '{argument.Name}'.";
                    return false;
                }

                return true;
            case MacroArgumentType.List:
                return TryPrepareList(argument, suppliedValue, $"list:{argument.ListName}", out value, out error);
            case MacroArgumentType.SecretList:
                return TryPrepareList(argument, suppliedValue, "list:secrets", out value, out error);
            case MacroArgumentType.ValueList:
                return TryPrepareValueList(argument, suppliedValue, out value, out error);
            default:
                error = $"Macro argument '{argument.Name}' has an unsupported type.";
                return false;
        }
    }

    private bool TryPrepareList(
        MacroArgumentDefinition argument,
        string suppliedValue,
        string reference,
        out string value,
        out string? error)
    {
        var selectedValues = suppliedValue.Split(',');
        var availableValues = _valueProvider.GetValues(reference);
        if (selectedValues.Any(item => string.IsNullOrWhiteSpace(item)
            || !availableValues.Contains(item, StringComparer.OrdinalIgnoreCase)))
        {
            value = string.Empty;
            error = $"Macro argument '{argument.Name}' contains a value that is not in its list.";
            return false;
        }

        if (argument.ListMode == ListSelectionMode.Single && selectedValues.Length != 1)
        {
            value = string.Empty;
            error = $"Macro argument '{argument.Name}' accepts only one list value.";
            return false;
        }

        if (selectedValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedValues.Length)
        {
            value = string.Empty;
            error = $"Macro argument '{argument.Name}' contains a duplicate list value.";
            return false;
        }

        value = string.Join(',', selectedValues);
        error = null;
        return true;
    }

    private static bool TryPrepareValueList(
        MacroArgumentDefinition argument,
        string suppliedValue,
        out string value,
        out string? error)
    {
        var values = suppliedValue.Split(',');
        if (values.Any(item => string.IsNullOrWhiteSpace(item)))
        {
            value = string.Empty;
            error = $"Macro argument '{argument.Name}' contains an empty list value.";
            return false;
        }

        if (argument.ListMode == ListSelectionMode.Single && values.Length != 1)
        {
            value = string.Empty;
            error = $"Macro argument '{argument.Name}' accepts only one value.";
            return false;
        }

        value = string.Join(',', values);
        error = null;
        return true;
    }

    private static MacroArgumentBindingResult Failure(string error)
        => new(false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), error);
}
