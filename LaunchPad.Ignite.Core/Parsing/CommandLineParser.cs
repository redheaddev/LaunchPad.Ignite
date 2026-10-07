using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Parsing;

public sealed class CommandLineParser
{
    private readonly CommandCatalog _catalog;
    private readonly IArgumentValueProvider _valueProvider;
    private readonly CommandLineTokenizer _tokenizer;

    public CommandLineParser(CommandCatalog catalog, IArgumentValueProvider valueProvider)
    {
        _catalog = catalog;
        _valueProvider = valueProvider;
        _tokenizer = new CommandLineTokenizer();
    }

    public CommandParseResult Parse(string input)
    {
        var tokens = _tokenizer.Tokenize(input);
        if (tokens.Count == 0)
        {
            return new CommandParseResult(CommandParseStatus.Incomplete);
        }

        if (!_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return new CommandParseResult(CommandParseStatus.Invalid, Error: $"Unknown command '{tokens[0].Value}'.");
        }

        var actionTokenIndex = 1;
        CommandActionDefinition? action = null;
        var argumentStartIndex = 1;

        if (tokens.Count > actionTokenIndex
            && _catalog.TryGetAction(command, tokens[actionTokenIndex].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
            argumentStartIndex = 2;
        }
        else if (_catalog.TryGetDefaultAction(command, out var defaultAction))
        {
            action = defaultAction;
        }
        else if (tokens.Count == 1)
        {
            return new CommandParseResult(CommandParseStatus.Incomplete);
        }
        else
        {
            return new CommandParseResult(
                CommandParseStatus.Invalid,
                Error: $"Unknown action '{tokens[actionTokenIndex].Value}' for command '{command.Name}'.");
        }

        var argumentTokens = tokens.Skip(argumentStartIndex).ToArray();
        if (argumentTokens.Length > action.Arguments.Count)
        {
            return new CommandParseResult(CommandParseStatus.Invalid, Error: "Too many arguments.");
        }

        var parsedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < action.Arguments.Count; index++)
        {
            var argument = action.Arguments[index];
            if (index >= argumentTokens.Length)
            {
                if (!argument.Optional)
                {
                    return new CommandParseResult(CommandParseStatus.Incomplete);
                }

                continue;
            }

            var value = argumentTokens[index].Value;
            if (!ValidateValue(argument, value, out var error))
            {
                return new CommandParseResult(CommandParseStatus.Invalid, Error: error);
            }

            parsedArguments.Add(argument.Name, value);
        }

        var parsedCommand = new ParsedCommand(command, action, parsedArguments);
        return new CommandParseResult(CommandParseStatus.Valid, parsedCommand);
    }

    private bool ValidateValue(CommandArgumentDefinition argument, string value, out string? error)
    {
        error = null;
        if (argument.InputType is not null
            && string.Equals(argument.InputType, "csv", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length == 0 || value.Split(',').Any(string.IsNullOrWhiteSpace))
            {
                error = $"Argument '{argument.Name}' contains an empty CSV value.";
                return false;
            }
        }

        if (argument.Ref is null)
        {
            return true;
        }

        var separator = argument.Ref.IndexOf(':');
        var refType = argument.Ref[..separator];
        var validValues = _valueProvider.GetValues(argument.Ref);
        var allowCustomEnumValue = argument.AllowCustomValues
            && string.Equals(refType, "enum", StringComparison.OrdinalIgnoreCase);
        var selections = string.Equals(refType, "list", StringComparison.OrdinalIgnoreCase)
            ? value.Split(',')
            : [value];

        if (selections.Any(selection => string.IsNullOrWhiteSpace(selection)
            || (!allowCustomEnumValue && !validValues.Contains(selection, StringComparer.OrdinalIgnoreCase))))
        {
            error = $"Argument '{argument.Name}' contains an unknown value.";
            return false;
        }

        if (selections.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selections.Length)
        {
            error = $"Argument '{argument.Name}' contains duplicate values.";
            return false;
        }

        return true;
    }
}
