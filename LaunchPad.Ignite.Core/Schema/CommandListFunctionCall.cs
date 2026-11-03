using System.Text.RegularExpressions;

namespace LaunchPad.Ignite.Core.Schema;

public sealed record CommandListFunctionCall(string FunctionName, IReadOnlyList<string> ArgumentNames)
{
    private static readonly Regex CallPattern = new(
        "^@(?<name>[A-Za-z_][A-Za-z0-9_:-]*)\\((?<arguments>.*)\\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ArgumentPattern = new("^<(?<name>[A-Za-z_][A-Za-z0-9_]*)>$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParseInvocation(string? expression, out CommandListFunctionInvocation invocation)
    {
        invocation = null!;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var value = expression.Trim();
        if (!value.StartsWith('@'))
        {
            return false;
        }

        var openParenthesis = value.IndexOf('(');
        if (openParenthesis <= 1 || value[^1] != ')')
        {
            return false;
        }

        var name = value[1..openParenthesis];
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_:-]*$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!TryParseArguments(value[(openParenthesis + 1)..^1], out var arguments))
        {
            return false;
        }

        invocation = new CommandListFunctionInvocation(name, arguments);
        return true;
    }

    private static bool TryParseArguments(string rawArguments, out IReadOnlyList<string> arguments)
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
                if (!TryAddArgument(rawArguments[argumentStart..index], parsedArguments))
                {
                    arguments = [];
                    return false;
                }

                argumentStart = index + 1;
            }
        }

        if (inQuotes || nesting != 0 || !TryAddArgument(rawArguments[argumentStart..], parsedArguments))
        {
            arguments = [];
            return false;
        }

        arguments = parsedArguments;
        return true;
    }

    private static bool TryAddArgument(string rawArgument, ICollection<string> arguments)
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

    public static bool TryParse(string? expression, out CommandListFunctionCall call)
    {
        call = null!;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var match = CallPattern.Match(expression.Trim());
        if (!match.Success)
        {
            return false;
        }

        var rawArguments = match.Groups["arguments"].Value.Trim();
        var argumentNames = new List<string>();
        if (rawArguments.Length > 0)
        {
            foreach (var rawArgument in rawArguments.Split(','))
            {
                var argumentMatch = ArgumentPattern.Match(rawArgument.Trim());
                if (!argumentMatch.Success)
                {
                    return false;
                }

                argumentNames.Add(argumentMatch.Groups["name"].Value);
            }
        }

        call = new CommandListFunctionCall(match.Groups["name"].Value, argumentNames);
        return true;
    }
}

public sealed record CommandListFunctionInvocation(string FunctionName, IReadOnlyList<string> Arguments);
