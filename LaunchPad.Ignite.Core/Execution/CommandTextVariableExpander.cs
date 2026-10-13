using System.Text.RegularExpressions;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Execution;

public sealed record CommandTextExpansionResult(
    string ExpandedText,
    IReadOnlyList<string> ResolvedVariables,
    IReadOnlyList<string> UnresolvedVariables);

public sealed class CommandTextVariableExpander
{
    private static readonly Regex VariablePattern = new(@"\$(?!\\)([^\s/<>?&#]+)", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _variables;

    public CommandTextVariableExpander(IEnumerable<CommandVariableDefinition>? variables = null)
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

    public CommandTextExpansionResult Expand(string commandText)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expandedText = VariablePattern.Replace(commandText, match =>
        {
            var name = match.Groups[1].Value;
            if (!_variables.TryGetValue(name, out var value))
            {
                unresolved.Add(name);
                return match.Value;
            }

            resolved.Add(name);
            return value;
        });

        return new CommandTextExpansionResult(expandedText, resolved.ToArray(), unresolved.ToArray());
    }
}
