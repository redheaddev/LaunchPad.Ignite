using System.Text;
using System.Text.Json;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Execution;

public interface ICommandFunctionExecutor
{
    Task<string> ExecuteAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class CommandTextFunctionExpander
{
    private readonly IReadOnlyDictionary<string, CommandFunctionDefinition> _functions;
    private readonly ICommandFunctionExecutor _executor;

    public CommandTextFunctionExpander(
        IEnumerable<CommandFunctionDefinition> functions,
        ICommandFunctionExecutor executor)
    {
        _functions = functions.ToDictionary(
            function => function.QualifiedName,
            StringComparer.OrdinalIgnoreCase);
        _executor = executor;
    }

    public async Task<string> ExpandAsync(string commandText, CancellationToken cancellationToken = default)
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        return await ExpandFragmentAsync(commandText, cache, cancellationToken);
    }

    private async Task<string> ExpandFragmentAsync(
        string text,
        IDictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (!TryReadCall(text, index, out var functionName, out var endIndex, out var rawArguments))
            {
                result.Append(text[index++]);
                continue;
            }

            if (!_functions.TryGetValue(functionName, out var function))
            {
                throw new InvalidOperationException($"Function '{functionName}' is not registered.");
            }

            var arguments = new string[rawArguments.Count];
            for (var argumentIndex = 0; argumentIndex < rawArguments.Count; argumentIndex++)
            {
                var rawArgument = rawArguments[argumentIndex].Trim();
                arguments[argumentIndex] = IsQuoted(rawArgument)
                    ? ParseQuotedArgument(rawArgument)
                    : await ExpandFragmentAsync(rawArgument, cache, cancellationToken);
            }

            var requiredArgumentCount = function.Parameters.Count(parameter => !parameter.Optional);
            if (arguments.Length < requiredArgumentCount || arguments.Length > function.Parameters.Count)
            {
                var expectedArguments = requiredArgumentCount == function.Parameters.Count
                    ? $"exactly {function.Parameters.Count}"
                    : $"between {requiredArgumentCount} and {function.Parameters.Count}";
                throw new FormatException(
                    $"Function '{function.QualifiedName}' expects {expectedArguments} argument(s), but received {arguments.Length}.");
            }

            var cacheKey = CreateCacheKey(function.QualifiedName, arguments);
            if (!cache.TryGetValue(cacheKey, out var value))
            {
                value = await _executor.ExecuteAsync(function, arguments, cancellationToken);
                cache[cacheKey] = value;
            }

            result.Append(value);
            index = endIndex;
        }

        return result.ToString();
    }

    private static bool TryReadCall(
        string text,
        int startIndex,
        out string functionName,
        out int endIndex,
        out IReadOnlyList<string> arguments)
    {
        functionName = string.Empty;
        endIndex = startIndex;
        arguments = [];
        if (text[startIndex] != '@' || startIndex + 1 >= text.Length || !IsNameStart(text[startIndex + 1]))
        {
            return false;
        }

        var nameEnd = startIndex + 2;
        while (nameEnd < text.Length && IsNameCharacter(text[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd >= text.Length || text[nameEnd] != '(')
        {
            return false;
        }

        functionName = text[(startIndex + 1)..nameEnd];
        var parsedArguments = new List<string>();
        var argumentStart = nameEnd + 1;
        var nesting = 0;
        var inQuotes = false;
        var escaped = false;

        for (var index = argumentStart; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == '(')
            {
                nesting++;
            }
            else if (character == ')')
            {
                if (nesting > 0)
                {
                    nesting--;
                    continue;
                }

                if (index > argumentStart || parsedArguments.Count > 0)
                {
                    AddArgument(text, argumentStart, index, parsedArguments);
                }

                endIndex = index + 1;
                arguments = parsedArguments;
                return true;
            }
            else if (character == ',' && nesting == 0)
            {
                AddArgument(text, argumentStart, index, parsedArguments);
                argumentStart = index + 1;
            }
        }

        throw new FormatException($"Function call '{functionName}' is missing a closing parenthesis.");
    }

    private static void AddArgument(string text, int start, int end, ICollection<string> arguments)
    {
        var value = text[start..end].Trim();
        if (value.Length == 0)
        {
            throw new FormatException("Function arguments cannot be empty.");
        }

        arguments.Add(value);
    }

    private static bool IsQuoted(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"';

    private static string ParseQuotedArgument(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(value)
                ?? throw new FormatException("Function string arguments cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("Function string arguments must use valid JSON quoting.", exception);
        }
    }

    private static string CreateCacheKey(string functionName, IReadOnlyList<string> arguments)
        => JsonSerializer.Serialize(new CacheKey(functionName.ToUpperInvariant(), arguments));

    private static bool IsNameStart(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';

    private static bool IsNameCharacter(char character)
        => IsNameStart(character) || character is >= '0' and <= '9' or '-' or ':';

    private sealed record CacheKey(string FunctionName, IReadOnlyList<string> Arguments);
}
