using System;
using System.IO;
using System.Linq;
using System.Text;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class ProfileCommandLogger
{
    private readonly string _profileDirectory;
    private readonly CommandLineTokenizer _tokenizer = new();

    public ProfileCommandLogger(string? profileDirectory = null)
    {
        _profileDirectory = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite");
    }

    public void StartSession()
    {
        TryAppend("log.txt", $"========== Session started {DateTimeOffset.Now:O} ==========");
    }

    public void RecordCommand(
        string commandText,
        bool succeeded,
        CommandCatalog catalog,
        DateTimeOffset? runTime = null)
    {
        var timestamp = runTime ?? DateTimeOffset.Now;
        var safeCommand = Redact(commandText, catalog)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        var entry = string.IsNullOrWhiteSpace(safeCommand) ? "(empty command)" : safeCommand;
        TryAppend("log.txt", $"{timestamp:O} [{(succeeded ? "SUCCESS" : "FAILED")}] {entry}");

        if (succeeded)
        {
            TryAppend("history.txt", $"{timestamp:O} {entry}");
        }
    }

    private string Redact(string commandText, CommandCatalog catalog)
    {
        var tokens = _tokenizer.Tokenize(commandText);
        if (tokens.Count == 0 || !catalog.TryGetCommand(tokens[0].Value, out var command))
        {
            return commandText;
        }

        var argumentStartIndex = 1;
        CommandActionDefinition? action = null;
        if (tokens.Count > 1
            && catalog.TryGetAction(command, tokens[1].Value, out var explicitAction)
            && !string.Equals(explicitAction.Name, "default", StringComparison.OrdinalIgnoreCase))
        {
            action = explicitAction;
            argumentStartIndex = 2;
        }
        else
        {
            catalog.TryGetDefaultAction(command, out action!);
        }

        if (action is null || argumentStartIndex >= tokens.Count)
        {
            return commandText;
        }

        var redactAllArguments = string.Equals(command.Kind, "secret", StringComparison.OrdinalIgnoreCase);
        var suppliedArguments = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextPositionalIndex = 0;
        var redactedTokens = new System.Collections.Generic.List<CommandLineToken>();
        foreach (var token in tokens.Skip(argumentStartIndex))
        {
            var equalsIndex = token.Value.IndexOf('=');
            CommandArgumentDefinition? argument;
            if (equalsIndex >= 0)
            {
                argument = action.Arguments.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, token.Value[..equalsIndex], StringComparison.OrdinalIgnoreCase));
                if (argument is not null)
                {
                    suppliedArguments.Add(argument.Name);
                }
            }
            else
            {
                while (nextPositionalIndex < action.Arguments.Count
                    && suppliedArguments.Contains(action.Arguments[nextPositionalIndex].Name))
                {
                    nextPositionalIndex++;
                }

                argument = nextPositionalIndex < action.Arguments.Count
                    ? action.Arguments[nextPositionalIndex++]
                    : null;
                if (argument is not null)
                {
                    suppliedArguments.Add(argument.Name);
                }
            }

            if (redactAllArguments || argument?.Sensitive == true)
            {
                redactedTokens.Add(token);
            }
        }

        if (redactedTokens.Count == 0)
        {
            return commandText;
        }

        var result = new StringBuilder(commandText);
        foreach (var token in redactedTokens.OrderByDescending(token => token.Start))
        {
            result.Remove(token.Start, token.Length);
            result.Insert(token.Start, "[REDACTED]");
        }

        return result.ToString();
    }

    private void TryAppend(string fileName, string line)
    {
        try
        {
            Directory.CreateDirectory(_profileDirectory);
            File.AppendAllText(Path.Combine(_profileDirectory, fileName), line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}