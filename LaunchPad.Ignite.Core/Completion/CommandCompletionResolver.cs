using LaunchPad.Ignite.Core.Lists;
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
    private Func<CommandDefinition, bool>? _commandFilter;

    public CommandCompletionResolver(
        CommandCatalog catalog,
        IArgumentValueProvider valueProvider,
        IEnumerable<CommandVariableDefinition>? variables = null,
        IEnumerable<CommandFunctionDefinition>? functions = null)
    {
        _catalog = catalog;
        _valueProvider = valueProvider;
        _variables = (variables ?? []).ToArray();
        _functions = (functions ?? []).ToArray();
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
            var currentToken = tokens[^1];
            var variableStart = currentToken.Value.LastIndexOf('$');
            var functionStart = currentToken.Value.LastIndexOf('@');
            if (functionStart > variableStart
                && currentToken.Value.IndexOf('(', functionStart + 1) < 0)
            {
                var prefix = currentToken.Value[functionStart..];
                var matchingFunctions = _functions
                    .Where(function => $"@{function.QualifiedName}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return CreateResolution(
                    CompletionKind.Function,
                    prefix,
                    currentToken.Start + functionStart,
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
                var prefix = currentToken.Value[variableStart..];
                var matchingVariables = _variables
                    .Where(variable => $"${variable.QualifiedName}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return CreateResolution(
                    CompletionKind.Variable,
                    prefix,
                    currentToken.Start + variableStart,
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

        var argumentIndex = trailingWhitespace
            ? argumentTokens.Length
            : Math.Max(0, argumentTokens.Length - 1);
        var currentPrefix = trailingWhitespace || argumentTokens.Length == 0
            ? string.Empty
            : argumentTokens[^1].Value;
        var tokenStart = trailingWhitespace || argumentTokens.Length == 0
            ? input.Length
            : argumentTokens[^1].Start;
        if (argumentIndex >= action.Arguments.Count)
        {
            return None(tokenStart);
        }

        var argument = action.Arguments[argumentIndex];
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
