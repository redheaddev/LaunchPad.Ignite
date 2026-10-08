using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Completion;

internal sealed class CommandCompletionResolver
{
    private readonly CommandCatalog _catalog;
    private readonly IArgumentValueProvider _valueProvider;
    private readonly CommandLineTokenizer _tokenizer = new();

    public CommandCompletionResolver(CommandCatalog catalog, IArgumentValueProvider valueProvider)
    {
        _catalog = catalog;
        _valueProvider = valueProvider;
    }

    public CompletionResolution Resolve(string input)
    {
        var tokens = _tokenizer.Tokenize(input);
        var trailingWhitespace = _tokenizer.EndsWithWhitespace(input);

        if (tokens.Count == 0)
        {
            return CreateResolution(CompletionKind.Command, string.Empty, input.Length,
                _catalog.Commands.Select(command => command.QualifiedName).ToArray());
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
                    .Select(candidate => candidate.QualifiedName)
                    .Where(name => name.StartsWith(tokens[0].Value, StringComparison.OrdinalIgnoreCase))
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
            ListSelectionMode.Normal,
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
        => new(kind, prefix, tokenStart, candidates, false, ListSelectionMode.Normal, false, false);

    private static CompletionResolution None(int tokenStart, bool awaitingSeparator = false)
        => CreateResolution(CompletionKind.None, string.Empty, tokenStart, Array.Empty<string>()) with
        {
            AwaitingSeparator = awaitingSeparator
        };
}
