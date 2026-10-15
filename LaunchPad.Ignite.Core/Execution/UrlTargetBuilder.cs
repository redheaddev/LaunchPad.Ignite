using System.Text.RegularExpressions;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Execution;

public sealed class UrlTargetBuilder
{
    private static readonly Regex PlaceholderPattern = new("<([^<>]+)>", RegexOptions.Compiled);
    private static readonly Regex VariablePattern = new(@"\$([^\s/<>?&#]+)", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _variables;

    public UrlTargetBuilder(IEnumerable<CommandVariableDefinition>? variables = null)
    {
        _variables = (variables ?? [])
            .ToDictionary(variable => variable.QualifiedName, variable => variable.Value, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateVariables(IEnumerable<CommandVariableDefinition> variables)
    {
        _variables.Clear();
        foreach (var variable in variables)
        {
            _variables[variable.QualifiedName] = variable.Value;
        }
    }

    public UrlTargetBuildResult Build(CommandParseResult parseResult)
    {
        var parsedCommand = parseResult.ParsedCommand;
        if (parseResult.Status != CommandParseStatus.Valid || parsedCommand is null)
        {
            return new UrlTargetBuildResult([], "A valid parsed command is required to build URL targets.");
        }

        return Build(parsedCommand);
    }

    public UrlTargetBuildResult Build(ParsedCommand parsedCommand)
    {
        if (!string.Equals(parsedCommand.Definition.ExecutionHandler, "url", StringComparison.OrdinalIgnoreCase))
        {
            return new UrlTargetBuildResult([], $"Command handler '{parsedCommand.Definition.ExecutionHandler}' is not supported.");
        }

        var action = parsedCommand.Action;
        var arguments = action.Arguments.ToDictionary(argument => argument.Name, StringComparer.OrdinalIgnoreCase);
        var placeholders = PlaceholderPattern.Matches(action.TargetTemplate)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var unknownPlaceholder = placeholders.FirstOrDefault(name => !arguments.ContainsKey(name));
        if (unknownPlaceholder is not null)
        {
            return new UrlTargetBuildResult([], $"Target contains unknown argument placeholder '<{unknownPlaceholder}>'.");
        }

        var variableNames = VariablePattern.Matches(action.TargetTemplate)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unknownVariable = variableNames.FirstOrDefault(name => !_variables.ContainsKey(name));
        if (unknownVariable is not null)
        {
            return new UrlTargetBuildResult([], $"Target contains unknown variable token '${unknownVariable}'.");
        }

        var valueSets = new List<(string Name, IReadOnlyList<string?> Values)>();
        if (action.Execution?.Mode == CommandExecutionMode.TargetPerValue)
        {
            foreach (var name in action.Execution.Arguments)
            {
                var argument = arguments[name];
                var values = parsedCommand.Arguments.TryGetValue(name, out var csv)
                    ? csv.Split(',', StringSplitOptions.TrimEntries).Cast<string?>().ToArray()
                    : new string?[] { null };
                valueSets.Add((name, values));
            }
        }
        else
        {
            valueSets.AddRange(parsedCommand.Arguments.Select(pair =>
                (pair.Key, (IReadOnlyList<string?>)new string?[] { pair.Value })));
        }

        var combinations = ExpandValues(valueSets);
        if (combinations.Count == 0)
        {
            combinations.Add(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        }

        var targets = new List<UrlTarget>(combinations.Count);
        foreach (var combination in combinations)
        {
            var target = action.TargetTemplate;
            foreach (var placeholder in placeholders)
            {
                var value = combination.TryGetValue(placeholder, out var combinationValue)
                    ? combinationValue
                    : parsedCommand.Arguments.GetValueOrDefault(placeholder);
                target = target.Replace(
                    $"<{placeholder}>",
                    value is null ? string.Empty : Uri.EscapeDataString(value),
                    StringComparison.OrdinalIgnoreCase);
            }

            target = VariablePattern.Replace(target, match => _variables[match.Groups[1].Value]);

            target = RemoveEmptyQueryParameters(target);
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return new UrlTargetBuildResult([], $"Generated target '{target}' is not a valid HTTP or HTTPS URL.");
            }

            targets.Add(new UrlTarget(uri.AbsoluteUri));
        }

        return new UrlTargetBuildResult(targets);
    }

    private static List<Dictionary<string, string?>> ExpandValues(
        IReadOnlyList<(string Name, IReadOnlyList<string?> Values)> valueSets)
    {
        var combinations = new List<Dictionary<string, string?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var (name, values) in valueSets)
        {
            var expanded = new List<Dictionary<string, string?>>();
            foreach (var combination in combinations)
            {
                foreach (var value in values)
                {
                    var copy = new Dictionary<string, string?>(combination, StringComparer.OrdinalIgnoreCase)
                    {
                        [name] = value
                    };
                    expanded.Add(copy);
                }
            }

            combinations = expanded;
        }

        return combinations;
    }

    private static string RemoveEmptyQueryParameters(string target)
    {
        var fragmentIndex = target.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? target[fragmentIndex..] : string.Empty;
        var targetWithoutFragment = fragmentIndex >= 0 ? target[..fragmentIndex] : target;
        var queryIndex = targetWithoutFragment.IndexOf('?');
        if (queryIndex < 0)
        {
            return target;
        }

        var path = targetWithoutFragment[..queryIndex];
        var query = targetWithoutFragment[(queryIndex + 1)..];
        var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var equalsIndex = parameter.IndexOf('=');
                return equalsIndex < 0 || equalsIndex < parameter.Length - 1;
            })
            .ToArray();

        return parameters.Length == 0
            ? path + fragment
            : path + "?" + string.Join('&', parameters) + fragment;
    }
}
