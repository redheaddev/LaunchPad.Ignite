using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record CommandPackDependencies(
    IReadOnlyList<CommandListDefinition> Lists,
    IReadOnlyList<CommandVariableDefinition> Variables,
    IReadOnlyList<string> UnresolvedListReferences,
    IReadOnlyList<string> UnresolvedVariableReferences);

public sealed class CommandPackDependencyResolver
{
    private static readonly Regex VariablePattern = new(@"\$(?!\\)([^\s/<>?&#]+)", RegexOptions.Compiled);

    public CommandPackDependencies Resolve(
        IEnumerable<CommandDefinition> commands,
        IEnumerable<CommandListDefinition> availableLists,
        IEnumerable<CommandVariableDefinition> availableVariables)
    {
        var listMap = availableLists
            .GroupBy(list => list.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .SelectMany(list => new[] { list.QualifiedName, list.Name }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => (name, list)))
            .GroupBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().list, StringComparer.OrdinalIgnoreCase);
        var variableMap = availableVariables
            .GroupBy(variable => variable.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(variable => variable.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var resolvedLists = new Dictionary<string, CommandListDefinition>(StringComparer.OrdinalIgnoreCase);
        var resolvedVariables = new Dictionary<string, CommandVariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var unresolvedLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variableQueue = new Queue<string>();

        foreach (var command in commands)
        {
            foreach (var action in command.Actions)
            {
                foreach (var argument in action.Arguments)
                {
                    if (argument.Ref?.StartsWith("enum:", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    var listName = argument.Ref[5..];
                    if (listMap.TryGetValue(listName, out var list))
                    {
                        resolvedLists[list.QualifiedName] = list;
                    }
                    else
                    {
                        unresolvedLists.Add(listName);
                    }
                }

                foreach (var text in GetVariableBearingText(action))
                {
                    EnqueueVariableReferences(text, variableQueue);
                }
            }
        }

        while (variableQueue.TryDequeue(out var variableName))
        {
            if (resolvedVariables.ContainsKey(variableName))
            {
                continue;
            }

            if (!variableMap.TryGetValue(variableName, out var variable))
            {
                unresolvedVariables.Add(variableName);
                continue;
            }

            resolvedVariables[variable.QualifiedName] = variable;
            EnqueueVariableReferences(variable.Value, variableQueue);
        }

        return new CommandPackDependencies(
            resolvedLists.Values.OrderBy(list => list.QualifiedName, StringComparer.OrdinalIgnoreCase).ToArray(),
            resolvedVariables.Values.OrderBy(variable => variable.QualifiedName, StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolvedLists.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolvedVariables.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> GetVariableBearingText(CommandActionDefinition action)
    {
        yield return action.Usage;
        yield return action.TargetTemplate;
        foreach (var argumentTemplate in action.ArgumentTemplates)
        {
            yield return argumentTemplate;
        }

        if (action.Execution is not null)
        {
            foreach (var argument in action.Execution.Arguments)
            {
                yield return argument;
            }
        }
    }

    private static void EnqueueVariableReferences(string text, Queue<string> variableQueue)
    {
        foreach (Match match in VariablePattern.Matches(text))
        {
            variableQueue.Enqueue(match.Groups[1].Value);
        }
    }
}
