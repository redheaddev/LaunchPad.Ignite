using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services.DirectFiles;

public sealed record CommandArgumentPathContext(string Path, int TokenStart, int TokenLength, string ArgumentNamePrefix, bool IsQuoted);

public sealed class CommandArgumentPathCompletion
{
    private readonly DirectFilePathHandler _pathHandler;

    public CommandArgumentPathCompletion(DirectFilePathHandler pathHandler)
    {
        _pathHandler = pathHandler;
    }

    public bool TryGetContext(
        string inputText,
        CommandCatalog catalog,
        CommandLineTokenizer tokenizer,
        out CommandArgumentPathContext context)
    {
        context = null!;
        var tokens = tokenizer.Tokenize(inputText);
        if (tokens.Count < 2 || tokenizer.EndsWithWhitespace(inputText)
            || !catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return false;
        }

        var actionStart = 1;
        CommandActionDefinition? action;
        if (catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
            actionStart = 2;
        }
        else if (!catalog.TryGetDefaultAction(command, out action!))
        {
            return false;
        }

        if (tokens.Count <= actionStart)
        {
            return false;
        }

        var currentToken = tokens[^1];
        var equalsIndex = currentToken.Value.IndexOf('=');
        var argumentNamePrefix = string.Empty;
        var argumentIndex = -1;
        string path;
        if (equalsIndex > 0)
        {
            argumentNamePrefix = currentToken.Value[..(equalsIndex + 1)];
            argumentIndex = action.Arguments.ToList().FindIndex(argument =>
                string.Equals(argument.Name, currentToken.Value[..equalsIndex], StringComparison.OrdinalIgnoreCase));
            if (argumentIndex < 0)
            {
                return false;
            }

            path = currentToken.Value[(equalsIndex + 1)..];
        }
        else
        {
            var suppliedNamedArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = actionStart; index < tokens.Count - 1; index++)
            {
                var priorEqualsIndex = tokens[index].Value.IndexOf('=');
                if (priorEqualsIndex > 0)
                {
                    suppliedNamedArguments.Add(tokens[index].Value[..priorEqualsIndex]);
                }
            }

            var positionalIndex = 0;
            for (var index = actionStart; index < tokens.Count - 1; index++)
            {
                if (tokens[index].Value.Contains('='))
                {
                    continue;
                }

                while (positionalIndex < action.Arguments.Count
                    && suppliedNamedArguments.Contains(action.Arguments[positionalIndex].Name))
                {
                    positionalIndex++;
                }

                positionalIndex++;
            }

            while (positionalIndex < action.Arguments.Count
                && suppliedNamedArguments.Contains(action.Arguments[positionalIndex].Name))
            {
                positionalIndex++;
            }

            argumentIndex = positionalIndex;
            path = currentToken.Value;
        }

        if (argumentIndex >= action.Arguments.Count || !_pathHandler.TryGetCompletionPath(path, out path))
        {
            return false;
        }

        context = new CommandArgumentPathContext(
            path,
            currentToken.Start,
            currentToken.Length,
            argumentNamePrefix,
            inputText.AsSpan(currentToken.Start, currentToken.Length).Contains('"'));
        return true;
    }

    public static string ReplaceValue(string inputText, CommandArgumentPathContext context, string selectedPath)
    {
        var shouldQuote = context.IsQuoted || selectedPath.Any(char.IsWhiteSpace);
        var replacement = context.ArgumentNamePrefix
            + (shouldQuote ? $"\"{selectedPath.Trim('"')}\"" : selectedPath);
        return inputText[..context.TokenStart]
            + replacement
            + inputText[(context.TokenStart + context.TokenLength)..];
    }
}
