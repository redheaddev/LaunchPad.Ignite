using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

internal sealed class CommandCompletionResolver
{
    private readonly CommandCatalog _catalog;
    private readonly IArgumentValueProvider _valueProvider;
    private readonly CommandLineTokenizer _tokenizer = new();
    private IReadOnlyList<CommandVariableDefinition> _variables;
    private readonly IReadOnlyList<CommandFunctionDefinition> _functions;
    private readonly ICommandListFunctionValueProvider? _listFunctionValueProvider;
    private Func<CommandDefinition, bool>? _commandFilter;

    public CommandCompletionResolver(
        CommandCatalog catalog,
        IArgumentValueProvider valueProvider,
        IEnumerable<CommandVariableDefinition>? variables = null,
        IEnumerable<CommandFunctionDefinition>? functions = null,
        ICommandListFunctionValueProvider? listFunctionValueProvider = null)
    {
        _catalog = catalog;
        _valueProvider = valueProvider;
        _variables = (variables ?? []).ToArray();
        _functions = (functions ?? []).ToArray();
        _listFunctionValueProvider = listFunctionValueProvider;
    }

    public void UpdateVariables(IEnumerable<CommandVariableDefinition> variables)
        => _variables = variables.ToArray();

    public void UpdateCommandFilter(Func<CommandDefinition, bool>? commandFilter)
        => _commandFilter = commandFilter;

    public CompletionResolution Resolve(string input)
    {
        var tokens = _tokenizer.Tokenize(input);
        var trailingWhitespace = EndsWithUnquotedWhitespace(input);

        if (!trailingWhitespace && tokens.Count > 0)
        {
            var completionToken = tokens[^1];
            if (TryResolveStandaloneListFunction(input, out var standaloneListResolution))
            {
                return standaloneListResolution;
            }

            if (TryResolveFunctionArgument(input, out var functionArgumentResolution))
            {
                return functionArgumentResolution;
            }

            var variableStart = completionToken.Value.LastIndexOf('$');
            var functionStart = completionToken.Value.LastIndexOf('@');
            if (functionStart > variableStart
                && completionToken.Value.IndexOf('(', functionStart + 1) < 0)
            {
                var prefix = completionToken.Value[functionStart..];
                var matchingFunctions = _functions
                    .Where(function => $"@{function.QualifiedName}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return CreateResolution(
                    CompletionKind.Function,
                    prefix,
                    completionToken.Start + functionStart,
                    matchingFunctions.Select(function => $"@{function.QualifiedName}(").ToArray()) with
                {
                    CandidateDisplayValues = matchingFunctions
                        .Select(function =>
                        {
                            var parameters = string.Join(", ", function.Parameters.Select(parameter =>
                                parameter.Optional ? $"[{parameter.Name}]" : parameter.Name));
                            return $"@{function.QualifiedName}({parameters})";
                        })
                        .ToArray()
                };
            }

            if (variableStart >= 0)
            {
                var prefix = completionToken.Value[variableStart..];
                var matchingVariables = _variables
                    .Where(variable => $"${variable.QualifiedName}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return CreateResolution(
                    CompletionKind.Variable,
                    prefix,
                    completionToken.Start + variableStart,
                    matchingVariables.Select(variable => $"${variable.QualifiedName}").ToArray()) with
                {
                    CandidateDisplayValues = matchingVariables
                        .Select(variable => $"${variable.QualifiedName} = {variable.Value}")
                        .ToArray()
                };
            }
        }

        if (tokens.Count == 0)
        {
            return CreateResolution(CompletionKind.Command, string.Empty, input.Length,
                _catalog.Commands
                    .Where(IsCommandAllowed)
                    .Select(command => command.QualifiedName)
                    .ToArray());
        }

        if (!trailingWhitespace && tokens.Count == 1
            && _catalog.TryGetCommand(tokens[0].Value, out _))
        {
            var matchingCommands = _catalog.Commands
                .Where(candidate => IsCommandAllowed(candidate)
                    && MatchesCommandPrefix(candidate, tokens[0].Value))
                .ToArray();
            if (matchingCommands.Length > 1)
            {
                return CreateResolution(
                    CompletionKind.Command,
                    tokens[0].Value,
                    tokens[0].Start,
                    matchingCommands.Select(candidate => candidate.QualifiedName).ToArray());
            }
        }

        if (!_catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            if (trailingWhitespace)
            {
                return None(input.Length);
            }

            return CreateResolution(
                CompletionKind.Command,
                tokens[0].Value,
                tokens[0].Start,
                _catalog.Commands
                    .Where(candidate => IsCommandAllowed(candidate)
                        && MatchesCommandPrefix(candidate, tokens[0].Value))
                    .Select(candidate => candidate.QualifiedName)
                    .ToArray());
        }

        var hasDefault = _catalog.TryGetDefaultAction(command, out var defaultAction);
        if (!trailingWhitespace && tokens.Count == 1
            && (hasDefault
                ? defaultAction.Arguments.Count > 0
                : command.Actions.Any(action => !string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase))))
        {
            return None(input.Length, awaitingSeparator: true);
        }

        CommandActionDefinition? action = null;
        var argumentStart = 1;

        if (!hasDefault)
        {
            if (tokens.Count == 1)
            {
                return ResolveAction(input, tokens, command, string.Empty, input.Length);
            }

            if (_catalog.TryGetAction(command, tokens[1].Value, out var foundAction)
                && !string.Equals(foundAction.Name, "default", StringComparison.OrdinalIgnoreCase))
            {
                action = foundAction;
                argumentStart = 2;
            }
            else if (tokens.Count == 2 && !trailingWhitespace)
            {
                return ResolveAction(input, tokens, command, tokens[1].Value, tokens[1].Start);
            }
            else if (tokens.Count == 1 || (tokens.Count == 2 && trailingWhitespace))
            {
                return ResolveAction(input, tokens, command, string.Empty, input.Length);
            }
            else
            {
                return None(input.Length);
            }
        }
        else if (tokens.Count > 1
            && _catalog.TryGetAction(command, tokens[1].Value, out var foundAction)
            && !string.Equals(foundAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = foundAction;
            argumentStart = 2;
        }
        else
        {
            action = defaultAction;
        }

        action ??= defaultAction;
        if (action is null)
        {
            return None(input.Length);
        }

        var argumentTokens = tokens.Skip(argumentStart).ToArray();
        if (!trailingWhitespace && argumentTokens.Length == 0 && action.Arguments.Count > 0)
        {
            return None(input.Length, awaitingSeparator: true);
        }

        var currentArgumentToken = !trailingWhitespace && argumentTokens.Length > 0 ? argumentTokens[^1] : null;
        var precedingTokenCount = currentArgumentToken is null ? argumentTokens.Length : argumentTokens.Length - 1;
        var precedingArguments = GetArgumentValues(action, argumentTokens.Take(precedingTokenCount));
        var currentNamedArgument = currentArgumentToken is null ? null : GetNamedArgument(action, currentArgumentToken.Value);
        var argumentIndex = currentNamedArgument?.Index
            ?? GetNextArgumentIndex(action, precedingArguments);
        var currentPrefix = currentArgumentToken is null
            ? string.Empty
            : currentNamedArgument is null
                ? currentArgumentToken.Value
                : currentArgumentToken.Value[(currentNamedArgument.Value.EqualsIndex + 1)..];
        var tokenStart = currentArgumentToken is null
            ? input.Length
            : currentNamedArgument is null
                ? currentArgumentToken.Start
                : currentArgumentToken.Start + currentNamedArgument.Value.EqualsIndex + 1;
        if (argumentIndex >= action.Arguments.Count)
        {
            return None(tokenStart);
        }

        var argument = action.Arguments[argumentIndex];
        if (argument.ListFunction is not null)
        {
            if (!CommandListFunctionCall.TryParse(argument.ListFunction, out var functionCall)
                || _listFunctionValueProvider is null
                || functionCall.ArgumentNames.Any(name => !precedingArguments.ContainsKey(name)))
            {
                return None(tokenStart);
            }

            var functionArguments = functionCall.ArgumentNames
                .Select(name => precedingArguments[name])
                .ToArray();
            var listItems = _listFunctionValueProvider.GetOrStart(functionCall.FunctionName, functionArguments);
            var listCandidates = listItems.Select(item => item.Value).ToArray();
            return new CompletionResolution(
                CompletionKind.Argument,
                currentPrefix,
                tokenStart,
                listCandidates,
                true,
                argument.ListMode,
                false,
                argumentIndex + 1 < action.Arguments.Count,
                argument,
                argumentIndex)
            {
                CandidateDisplayValues = listItems.Select(item => item.DisplayText ?? item.Value).ToArray()
            };
        }

        if (argument.Ref is null)
        {
            return None(tokenStart);
        }

        var separator = argument.Ref.IndexOf(':');
        var refType = argument.Ref[..separator];
        var candidates = _valueProvider.GetValues(argument.Ref);
        var isList = string.Equals(refType, "list", StringComparison.OrdinalIgnoreCase);
        if (!isList)
        {
            candidates = candidates
                .Where(value => value.StartsWith(currentPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var hasNextArgument = argumentIndex + 1 < action.Arguments.Count;
        var nextArgumentRequired = hasNextArgument && !action.Arguments[argumentIndex + 1].Optional;
        return new CompletionResolution(
            CompletionKind.Argument,
            currentPrefix,
            tokenStart,
            candidates,
            isList,
            argument.ListMode,
            !isList && nextArgumentRequired,
            hasNextArgument,
            argument,
            argumentIndex);
    }

    private bool TryResolveStandaloneListFunction(string input, out CompletionResolution resolution)
    {
        resolution = None(input.Length);
        if (_listFunctionValueProvider is null || !input.StartsWith('@') || input.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var openParenthesis = input.IndexOf('(');
        if (openParenthesis <= 1 || input[^1] != ')')
        {
            return false;
        }

        var functionName = input[1..openParenthesis];
        var function = _functions.FirstOrDefault(candidate =>
            string.Equals(candidate.QualifiedName, functionName, StringComparison.OrdinalIgnoreCase));
        if (function?.ReturnType != CommandFunctionReturnType.List
            || !TryParseFunctionArguments(input[(openParenthesis + 1)..^1], out var arguments)
            || arguments.Count < function.Parameters.Count(parameter => !parameter.Optional)
            || arguments.Count > function.Parameters.Count)
        {
            return false;
        }

        var listItems = _listFunctionValueProvider.GetOrStart(function.QualifiedName, arguments);
        resolution = new CompletionResolution(
            CompletionKind.Function,
            string.Empty,
            0,
            listItems.Select(item => item.Value).ToArray(),
            true,
            ListSelectionMode.Single,
            false,
            false)
        {
            CandidateDisplayValues = listItems.Select(item => item.DisplayText ?? item.Value).ToArray()
        };
        return true;
    }

    private static bool TryParseFunctionArguments(string rawArguments, out IReadOnlyList<string> arguments)
    {
        var parsedArguments = new List<string>();
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            arguments = parsedArguments;
            return true;
        }

        var argumentStart = 0;
        var inQuotes = false;
        var nesting = 0;
        for (var index = 0; index < rawArguments.Length; index++)
        {
            var character = rawArguments[index];
            if (character == '"' && (index == 0 || rawArguments[index - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == '(')
            {
                nesting++;
            }
            else if (!inQuotes && character == ')')
            {
                if (nesting == 0)
                {
                    arguments = [];
                    return false;
                }

                nesting--;
            }
            else if (!inQuotes && nesting == 0 && character == ',')
            {
                if (!TryAddFunctionArgument(rawArguments[argumentStart..index], parsedArguments))
                {
                    arguments = [];
                    return false;
                }

                argumentStart = index + 1;
            }
        }

        if (inQuotes || nesting != 0 || !TryAddFunctionArgument(rawArguments[argumentStart..], parsedArguments))
        {
            arguments = [];
            return false;
        }

        arguments = parsedArguments;
        return true;
    }

    private static bool TryAddFunctionArgument(string rawArgument, ICollection<string> arguments)
    {
        var value = rawArgument.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
        {
            value = value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        arguments.Add(value);
        return true;
    }

    private bool TryResolveFunctionArgument(string input, out CompletionResolution resolution)
    {
        resolution = None(input.Length);
        var functionStart = input.LastIndexOf('@');
        if (functionStart < 0)
        {
            return false;
        }

        var nameStart = functionStart + 1;
        var openParenthesis = nameStart;
        while (openParenthesis < input.Length && (char.IsLetterOrDigit(input[openParenthesis]) || input[openParenthesis] is '_' or ':' or '-'))
        {
            openParenthesis++;
        }

        if (openParenthesis == nameStart || openParenthesis >= input.Length || input[openParenthesis] != '(')
        {
            return false;
        }

        var functionName = input[nameStart..openParenthesis];
        var function = _functions.FirstOrDefault(candidate =>
            string.Equals(candidate.QualifiedName, functionName, StringComparison.OrdinalIgnoreCase));
        if (function is null || function.Parameters.Count == 0)
        {
            return false;
        }

        var argumentStart = openParenthesis + 1;
        var argumentIndex = 0;
        var inQuotes = false;
        var nesting = 0;
        for (var index = argumentStart; index < input.Length; index++)
        {
            var character = input[index];
            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == '(')
            {
                nesting++;
            }
            else if (!inQuotes && character == ')')
            {
                if (nesting == 0)
                {
                    return false;
                }

                nesting--;
            }
            else if (!inQuotes && nesting == 0 && character == ',')
            {
                argumentIndex++;
                argumentStart = index + 1;
            }
        }

        if (argumentIndex >= function.Parameters.Count
            || function.Parameters[argumentIndex].ListFunction is not { } listFunctionSource
            || !CommandListFunctionCall.TryParse(listFunctionSource, out var listFunctionCall)
            || _listFunctionValueProvider is null)
        {
            return false;
        }

        var rawPriorArguments = input[(openParenthesis + 1)..argumentStart];
        var priorValues = rawPriorArguments.Length == 0
            ? []
            : rawPriorArguments.Split(',').Select(value => value.Trim().Trim('"')).ToArray();
        var priorArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Math.Min(argumentIndex, function.Parameters.Count); index++)
        {
            priorArguments[function.Parameters[index].Name] = priorValues[index];
        }

        if (listFunctionCall.ArgumentNames.Any(name => !priorArguments.ContainsKey(name)))
        {
            return false;
        }

        while (argumentStart < input.Length && char.IsWhiteSpace(input[argumentStart]))
        {
            argumentStart++;
        }

        var prefix = input[argumentStart..];
        if (prefix.StartsWith('"'))
        {
            argumentStart++;
            prefix = prefix[1..];
        }

        var functionArguments = listFunctionCall.ArgumentNames.Select(name => priorArguments[name]).ToArray();
        var listItems = _listFunctionValueProvider.GetOrStart(listFunctionCall.FunctionName, functionArguments);
        resolution = new CompletionResolution(
            CompletionKind.Function,
            prefix,
            argumentStart,
            listItems.Select(item => item.Value).ToArray(),
            true,
            ListSelectionMode.Single,
            false,
            false)
        {
            CandidateDisplayValues = listItems.Select(item => item.DisplayText ?? item.Value).ToArray()
        };
        return true;
    }

    private static Dictionary<string, string> GetArgumentValues(
        CommandActionDefinition action,
        IEnumerable<CommandLineToken> tokens)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nextPositionalIndex = 0;
        foreach (var token in tokens)
        {
            var equalsIndex = token.Value.IndexOf('=');
            if (equalsIndex > 0)
            {
                var namedArgument = action.Arguments.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, token.Value[..equalsIndex], StringComparison.OrdinalIgnoreCase));
                if (namedArgument is not null)
                {
                    values[namedArgument.Name] = token.Value[(equalsIndex + 1)..];
                    continue;
                }
            }

            nextPositionalIndex = GetNextArgumentIndex(action, values, nextPositionalIndex);
            if (nextPositionalIndex < action.Arguments.Count)
            {
                values[action.Arguments[nextPositionalIndex].Name] = token.Value;
                nextPositionalIndex++;
            }
        }

        return values;
    }

    private static (int Index, int EqualsIndex)? GetNamedArgument(CommandActionDefinition action, string token)
    {
        var equalsIndex = token.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return null;
        }

        for (var index = 0; index < action.Arguments.Count; index++)
        {
            if (string.Equals(action.Arguments[index].Name, token[..equalsIndex], StringComparison.OrdinalIgnoreCase))
            {
                return (index, equalsIndex);
            }
        }

        return null;
    }

    private static int GetNextArgumentIndex(CommandActionDefinition action, IReadOnlyDictionary<string, string> values)
        => GetNextArgumentIndex(action, values, 0);

    private static int GetNextArgumentIndex(
        CommandActionDefinition action,
        IReadOnlyDictionary<string, string> values,
        int startIndex)
    {
        while (startIndex < action.Arguments.Count && values.ContainsKey(action.Arguments[startIndex].Name))
        {
            startIndex++;
        }

        return startIndex;
    }

    private CompletionResolution ResolveAction(
        string input,
        IReadOnlyList<CommandLineToken> tokens,
        CommandDefinition command,
        string prefix,
        int tokenStart)
    {
        var candidates = command.Actions
            .Where(action => !string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase))
            .Select(action => action.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new CompletionResolution(
            CompletionKind.Action,
            prefix,
            tokenStart,
            candidates,
            false,
            ListSelectionMode.Multiple,
            candidates.Length > 0 && command.Actions
                .Where(action => !string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase))
                .Any(action => action.Arguments.Any(argument => !argument.Optional)),
            false);
    }

    private static CompletionResolution CreateResolution(
        CompletionKind kind,
        string prefix,
        int tokenStart,
        IReadOnlyList<string> candidates)
        => new(kind, prefix, tokenStart, candidates, false, ListSelectionMode.Multiple, false, false);

    private static bool MatchesCommandPrefix(CommandDefinition command, string prefix)
        => command.QualifiedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || command.Namespace?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;

    private bool IsCommandAllowed(CommandDefinition command)
        => _commandFilter?.Invoke(command) != false;

    private static bool EndsWithUnquotedWhitespace(string input)
    {
        var insideQuotes = false;
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '"')
            {
                insideQuotes = !insideQuotes;
            }
        }

        return !insideQuotes && input.Length > 0 && char.IsWhiteSpace(input[^1]);
    }

    private static CompletionResolution None(int tokenStart, bool awaitingSeparator = false)
        => CreateResolution(CompletionKind.None, string.Empty, tokenStart, Array.Empty<string>()) with
        {
            AwaitingSeparator = awaitingSeparator
        };
}
