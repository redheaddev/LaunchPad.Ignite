using System;
using System.Collections.Generic;
using System.Globalization;
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

    public IReadOnlyList<CommandHistoryEntry> ReadHistoryEntries()
    {
        try
        {
            var historyPath = Path.Combine(_profileDirectory, "history.txt");
            if (!File.Exists(historyPath))
            {
                return [];
            }

            return File.ReadLines(historyPath)
                .Select(ParseHistoryLine)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.CommandText))
                .Reverse()
                .GroupBy(entry => entry.CommandText, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<CommandHistoryEntry> FilterHistoryEntries(
        IReadOnlyList<CommandHistoryEntry> historyEntries,
        string inputText)
        => historyEntries
            .Where(entry => entry.CommandText.StartsWith(inputText, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToArray();

    public bool RemoveHistoryEntry(string commandText)
    {
        try
        {
            var historyPath = Path.Combine(_profileDirectory, "history.txt");
            if (!File.Exists(historyPath))
            {
                return false;
            }

            var lines = File.ReadAllLines(historyPath);
            var updatedLines = lines.Where(line =>
                !string.Equals(ParseHistoryLine(line).CommandText, commandText, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (updatedLines.Length == lines.Length)
            {
                return false;
            }

            File.WriteAllLines(historyPath, updatedLines);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool ClearHistory()
    {
        try
        {
            var historyPath = Path.Combine(_profileDirectory, "history.txt");
            if (!File.Exists(historyPath))
            {
                return true;
            }

            File.WriteAllText(historyPath, string.Empty);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string FormatHistoryAge(DateTimeOffset? runTime, DateTimeOffset now)
    {
        if (runTime is null)
        {
            return string.Empty;
        }

        var age = now - runTime.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}m";
        }

        return age < TimeSpan.FromDays(7)
            ? $"{(int)age.TotalHours}h"
            : $"{(int)age.TotalDays}d";
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

        if (succeeded && !MathExpressionParser.TryParse(commandText, out _))
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

        var suppliedArguments = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextPositionalIndex = 0;
        var redactedTokens = new System.Collections.Generic.List<(CommandLineToken Token, string Replacement)>();
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

            if (IsSensitiveArgument(argument))
            {
                var replacement = equalsIndex >= 0
                    ? $"{token.Value[..(equalsIndex + 1)]}[REDACTED]"
                    : "[REDACTED]";
                redactedTokens.Add((token, replacement));
            }
        }

        if (redactedTokens.Count == 0)
        {
            return commandText;
        }

        var result = new StringBuilder(commandText);
        foreach (var (token, replacement) in redactedTokens.OrderByDescending(item => item.Token.Start))
        {
            result.Remove(token.Start, token.Length);
            result.Insert(token.Start, replacement);
        }

        return result.ToString();
    }

    private static bool IsSensitiveArgument(CommandArgumentDefinition? argument)
        => argument is not null
            && (argument.Sensitive
                || argument.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || argument.Name.Contains("passwd", StringComparison.OrdinalIgnoreCase));

    private static CommandHistoryEntry ParseHistoryLine(string line)
    {
        var separator = line.IndexOf(' ');
        if (separator > 0
            && DateTimeOffset.TryParse(
                line[..separator],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var runTime))
        {
            return new CommandHistoryEntry(line[(separator + 1)..], runTime);
        }

        return new CommandHistoryEntry(line, null);
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

public sealed record CommandHistoryEntry(string CommandText, DateTimeOffset? RunTime);
